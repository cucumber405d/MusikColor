namespace MusikColor.Contracts;

/// <summary>Параметры инициализации, которые хост передаёт плагину.</summary>
public sealed class VisualizerContext
{
    public int BandCount { get; }

    public VisualizerContext(int bandCount)
    {
        BandCount = bandCount;
    }
}
