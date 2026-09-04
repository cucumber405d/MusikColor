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

    /// <summary>
    /// Срабатывает, если запись оборвалась из-за ошибки (например, WASAPI
    /// не смог инициализировать поток). Без этого события такие сбои
    /// проходят незаметно — поток просто тихо останавливается.
    /// </summary>
    event EventHandler<Exception>? ErrorOccurred;

    void Start();
    void Stop();
}
