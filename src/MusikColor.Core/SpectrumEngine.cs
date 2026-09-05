using System;
using MusikColor.Contracts;
using MusikColor.Core.Dsp;

namespace MusikColor.Core;

/// <summary>
/// Ядро приложения. Подписывается на "сырой" поток от IAudioSource,
/// превращает его в нормализованный поток частотных кадров и публикует
/// в IVisualizationSink. Ничего не знает ни о WASAPI, ни о рендеринге —
/// только математика и обвязка порт-адаптер.
/// </summary>
public sealed class SpectrumEngine : IDisposable
{
    private const int FftSize = 2048;
    private const int WaveformSize = 512; // децимированная форма волны для осциллографа

    private readonly IAudioSource _source;
    private readonly IVisualizationSink _sink;

    private readonly SlidingWindowBuffer _window;
    private readonly SlidingWindowBuffer _leftWindow;
    private readonly SlidingWindowBuffer _rightWindow;
    private readonly float[] _hann;
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly float[] _magnitude;
    private readonly float[] _bands;
    private readonly float[] _waveform;
    private readonly float[] _waveformLeft;
    private readonly float[] _waveformRight;
    private readonly BandMapper _bandMapper;
    private readonly Normalizer _normalizer;

    private readonly object _gate = new();
    private DateTime _lastPublish = DateTime.MinValue;
    private readonly TimeSpan _minPublishInterval = TimeSpan.FromMilliseconds(16); // ограничиваем анализ ~60 fps

    private float _volumeAverage;

    public SpectrumEngine(IAudioSource source, IVisualizationSink sink, int bandCount = 48)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

        _window = new SlidingWindowBuffer(FftSize);
        _leftWindow = new SlidingWindowBuffer(FftSize);
        _rightWindow = new SlidingWindowBuffer(FftSize);
        _hann = WindowFunctions.Hann(FftSize);
        _re = new float[FftSize];
        _im = new float[FftSize];
        _magnitude = new float[FftSize / 2];
        _bands = new float[bandCount];
        _waveform = new float[WaveformSize];
        _waveformLeft = new float[WaveformSize];
        _waveformRight = new float[WaveformSize];

        _bandMapper = new BandMapper(bandCount, FftSize, source.Format.SampleRate);
        _normalizer = new Normalizer(bandCount);

        _source.FrameAvailable += OnFrameAvailable;
    }

    public void Start() => _source.Start();

    public void Stop() => _source.Stop();

    private void OnFrameAvailable(object? sender, AudioFrame frame)
    {
        var mono = ToMono(frame.Samples, frame.Format.Channels);
        var left = ExtractChannel(frame.Samples, frame.Format.Channels, 0);
        var right = frame.Format.Channels > 1 ? ExtractChannel(frame.Samples, frame.Format.Channels, 1) : left;

        lock (_gate)
        {
            _window.Push(mono);
            _leftWindow.Push(left);
            _rightWindow.Push(right);

            var now = DateTime.UtcNow;
            if (!_window.IsFull || now - _lastPublish < _minPublishInterval)
            {
                return;
            }
            _lastPublish = now;

            Analyze(now);
        }
    }

    private void Analyze(DateTime timestamp)
    {
        var snapshot = _window.Snapshot();
        for (int i = 0; i < FftSize; i++)
        {
            _re[i] = snapshot[i] * _hann[i];
            _im[i] = 0f;
        }

        Fft.Forward(_re, _im);

        float sumSquares = 0f;
        for (int i = 0; i < _magnitude.Length; i++)
        {
            float re = _re[i];
            float im = _im[i];
            float mag = MathF.Sqrt(re * re + im * im) / FftSize;
            _magnitude[i] = mag;
            sumSquares += mag * mag;
        }

        _bandMapper.Map(_magnitude, _bands);
        _normalizer.Apply(_bands);

        float volume = MathF.Sqrt(sumSquares / _magnitude.Length);
        _volumeAverage = _volumeAverage <= 0f ? volume : _volumeAverage * 0.95f + volume * 0.05f;
        bool beat = volume > _volumeAverage * 1.6f && volume > 0.02f;

        BuildWaveform(snapshot, _waveform);
        BuildWaveform(_leftWindow.Snapshot(), _waveformLeft);
        BuildWaveform(_rightWindow.Snapshot(), _waveformRight);

        var frame = new FrequencyFrame(
            (float[])_bands.Clone(),
            Math.Clamp(volume * 6f, 0f, 1f),
            beat,
            (float[])_waveform.Clone(),
            (float[])_waveformLeft.Clone(),
            (float[])_waveformRight.Clone(),
            timestamp);
        _sink.Publish(frame);
    }

    /// <summary>
    /// Децимирует необработанное (до окна Ханна) окно сэмплов до
    /// фиксированного размера WaveformSize — усредняя блоки, а не просто
    /// прореживая, чтобы не терять пики и не давать "лесенку" на экране.
    /// </summary>
    private static void BuildWaveform(ReadOnlySpan<float> snapshot, float[] destination)
    {
        int step = snapshot.Length / destination.Length;
        if (step <= 0)
        {
            step = 1;
        }

        for (int i = 0; i < destination.Length; i++)
        {
            int start = i * step;
            if (start >= snapshot.Length)
            {
                destination[i] = 0f;
                continue;
            }

            int end = Math.Min(start + step, snapshot.Length);
            float sum = 0f;
            int count = 0;
            for (int j = start; j < end; j++)
            {
                sum += snapshot[j];
                count++;
            }
            destination[i] = count > 0 ? sum / count : 0f;
        }
    }

    /// <summary>Достаёт один канал из интерливинг-сэмплов (для вектороскопа).</summary>
    private static float[] ExtractChannel(float[] samples, int channels, int channelIndex)
    {
        if (channels <= 1)
        {
            return samples;
        }

        int frames = samples.Length / channels;
        var result = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            result[i] = samples[i * channels + channelIndex];
        }
        return result;
    }

    private static float[] ToMono(float[] samples, int channels)
    {
        if (channels <= 1)
        {
            return samples;
        }

        int frames = samples.Length / channels;
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
            {
                sum += samples[i * channels + c];
            }
            mono[i] = sum / channels;
        }
        return mono;
    }

    public void Dispose()
    {
        _source.FrameAvailable -= OnFrameAvailable;
        _source.Dispose();
    }
}
