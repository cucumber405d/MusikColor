using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using MusikColor.Contracts;

namespace MusikColor.Adapters.Capture;

/// <summary>
/// Захват "того, что играет" прямо со звуковой карты через WASAPI loopback —
/// встроенный в Windows режим (с Vista), виртуальный кабель не нужен.
/// </summary>
public sealed class LoopbackAudioSource : IAudioSource
{
    private readonly MMDevice _device;
    private WasapiLoopbackCapture? _capture;

    public string Name { get; }
    public AudioFormatInfo Format { get; }

    public event EventHandler<AudioFrame>? FrameAvailable;
    public event EventHandler<Exception>? ErrorOccurred;

    public LoopbackAudioSource(MMDevice? device = null)
    {
        _device = device ?? new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        Name = $"Системный звук: {_device.FriendlyName}";
        var mix = _device.AudioClient.MixFormat;
        Format = new AudioFormatInfo(mix.SampleRate, mix.Channels);
    }

    public void Start()
    {
        if (_capture != null)
        {
            return;
        }

        _capture = new WasapiLoopbackCapture(_device);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
    }

    public void Stop()
    {
        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
        }
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture == null)
        {
            return;
        }

        var samples = PcmConverter.ToFloatSamples(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
        FrameAvailable?.Invoke(this, new AudioFrame(samples, Format));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // NAudio не бросает исключение наружу при обрыве записи — оно приходит
        // именно сюда. Без этой подписки ошибка (например, WASAPI не смог
        // инициализировать поток) проходит незаметно.
        if (e.Exception != null)
        {
            ErrorOccurred?.Invoke(this, e.Exception);
        }
    }

    public void Dispose()
    {
        Stop();
        _device.Dispose();
    }
}
