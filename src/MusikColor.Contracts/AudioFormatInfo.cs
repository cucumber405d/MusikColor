namespace MusikColor.Contracts;

/// <summary>Формат аудиопотока: частота дискретизации и число каналов.</summary>
public sealed class AudioFormatInfo
{
    public int SampleRate { get; }
    public int Channels { get; }

    public AudioFormatInfo(int sampleRate, int channels)
    {
        SampleRate = sampleRate;
        Channels = channels;
    }
}
