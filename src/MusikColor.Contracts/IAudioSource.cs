using System;

namespace MusikColor.Contracts;

/// <summary>
/// Порт источника звука. Адаптеры (микрофон, WASAPI-перехват звуковой карты)
/// реализуют этот интерфейс — ядро ничего не знает об их внутренностях.
/// </summary>
public interface IAudioSource : IDisposable
{
    string Name { get; }
    AudioFormatInfo Format { get; }

    event EventHandler<AudioFrame>? FrameAvailable;

    void Start();
    void Stop();
}
