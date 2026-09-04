using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using MusikColor.Contracts;

namespace MusikColor.Adapters.Capture;

/// <summary>Захват с микрофона (или любого входного устройства) через WASAPI.</summary>
public sealed class MicrophoneAudioSource : IAudioSource
{
    private readonly MMDevice _device;
    private WasapiCapture? _capture;

    public string Name { get; }
    public AudioFormatInfo Format { get; }

    public event EventHandler<AudioFrame>? FrameAvailable;
    public event EventHandler<Exception>? ErrorOccurred;

    public MicrophoneAudioSource(MMDevice? device = null)
    {
        _device = device ?? new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        Name = $"Микрофон: {_device.FriendlyName}";
        var mix = _device.AudioClient.MixFormat;
        Format = new AudioFormatInfo(mix.SampleRate, mix.Channels);
    }

    public void Start()
    {
        if (_capture != null)
        {
            return;
        }

        _capture = new WasapiCapture(_device);
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
