namespace MusikColor.Contracts;

/// <summary>
/// Один "сырой" кусок аудио, пришедший от источника (микрофон или перехват
/// звуковой карты) — интерливинг-сэмплы в диапазоне [-1..1] плюс формат.
/// </summary>
public sealed class AudioFrame
{
    public float[] Samples { get; }
    public AudioFormatInfo Format { get; }

    public AudioFrame(float[] samples, AudioFormatInfo format)
    {
        Samples = samples;
        Format = format;
    }
}
