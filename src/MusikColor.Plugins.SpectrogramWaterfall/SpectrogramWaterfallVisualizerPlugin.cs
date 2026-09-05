using System;
using System.Runtime.InteropServices;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.SpectrogramWaterfall;

/// <summary>
/// "Спектрограмма-водопад" — как в профессиональных аудиоанализаторах
/// и SDR-приёмниках: по горизонтали — частота (бас слева, верха справа),
/// по вертикали — время. Новая строка появляется сверху и постепенно
/// уезжает вниз, пока не исчезнет с экрана.
///
/// Цвет — вся радуга (синий -> голубой -> жёлтый -> красный) по
/// громкости конкретной частоты. Диапазон яркости калибруется по
/// входящему сигналу самостоятельно: отслеживается недавний максимум
/// (растёт мгновенно, если частоты стали громче/шире, и медленно
/// спадает в тишине), и цвет каждой ячейки берётся относительно этого
/// плавающего максимума — иначе при типичной громкости почти всё
/// оставалось бы в тёмно-синей части шкалы.
/// </summary>
public sealed class SpectrogramWaterfallVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "spectrogram-waterfall";
    public string DisplayName => "Спектрограмма-водопад";

    private const int HistoryRows = 240;
    private const double RowIntervalMs = 22.0; // ~45 строк/сек, не зависит от Hz монитора

    private const float MaxDecayPerRow = 0.995f; // медленный спад плавающего максимума
    private const float MinRange = 0.03f;        // не даём диапазону схлопнуться до нуля

    private SKBitmap? _bitmap;
    private byte[] _pixels = Array.Empty<byte>();
    private int _bandCount;
    private int _rowBytes;
    private DateTime _lastRowTime = DateTime.MinValue;
    private float _runningMax = MinRange;

    public void Init(VisualizerContext context)
    {
        AllocateBitmap(Math.Max(1, context.BandCount));
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(2, 2, 10));

        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        var bands = frame.Bands;
        int n = bands.Length;
        if (n == 0)
        {
            return;
        }

        if (_bitmap == null || _bandCount != n)
        {
            AllocateBitmap(n);
        }

        var now = DateTime.UtcNow;
        if ((now - _lastRowTime).TotalMilliseconds >= RowIntervalMs)
        {
            _lastRowTime = now;
            PushNewRow(bands);
        }

        Marshal.Copy(_pixels, 0, _bitmap!.GetPixels(), _pixels.Length);

        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(_bitmap, new SKRect(0, 0, info.Width, info.Height), paint);
    }

    private void AllocateBitmap(int bandCount)
    {
        _bitmap?.Dispose();
        _bitmap = new SKBitmap(new SKImageInfo(bandCount, HistoryRows, SKColorType.Rgba8888, SKAlphaType.Opaque));
        _rowBytes = _bitmap.RowBytes;
        _pixels = new byte[_rowBytes * HistoryRows];
        _bandCount = bandCount;
        _lastRowTime = DateTime.MinValue;
        _runningMax = MinRange;
    }

    /// <summary>
    /// Сдвигает всю историю на одну строку вниз (memmove по массиву) и
    /// записывает новую строку наверх — так новые данные всегда
    /// появляются сверху и "стекают" вниз, как в настоящем водопаде.
    /// </summary>
    private void PushNewRow(float[] bands)
    {
        Buffer.BlockCopy(_pixels, 0, _pixels, _rowBytes, (HistoryRows - 1) * _rowBytes);

        // Калибровка: если в этом кадре пришла частота громче текущего
        // плавающего максимума — диапазон расширяется мгновенно. Если
        // нет — максимум медленно "забывается" (спадает), чтобы после
        // громкого пика тихая музыка снова красилась во всю радугу, а не
        // тонула в тёмно-синем из-за старого пика.
        float frameMax = 0f;
        for (int i = 0; i < bands.Length; i++)
        {
            if (bands[i] > frameMax)
            {
                frameMax = bands[i];
            }
        }
        _runningMax = Math.Max(frameMax, Math.Max(_runningMax * MaxDecayPerRow, MinRange));

        for (int i = 0; i < _bandCount && i < bands.Length; i++)
        {
            float normalized = bands[i] / _runningMax;
            var color = ColorMapFor(normalized);
            int offset = i * 4;
            _pixels[offset + 0] = color.Red;
            _pixels[offset + 1] = color.Green;
            _pixels[offset + 2] = color.Blue;
            _pixels[offset + 3] = 255;
        }
    }

    private static SKColor ColorMapFor(float value)
    {
        float v = Math.Clamp(value, 0f, 1f);

        // Вся радуга: синий (тихо) -> голубой -> зелёный -> жёлтый ->
        // красный (громко), плюс яркость растёт вместе с громкостью —
        // тихие частоты не просто другого цвета, а ещё и тусклее.
        float hue = (1f - v) * 240f;
        float saturation = 92f;
        float brightness = 28f + v * 72f;
        return SKColor.FromHsv(hue, saturation, brightness);
    }
}
