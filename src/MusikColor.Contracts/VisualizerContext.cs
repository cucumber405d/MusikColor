using System;

namespace MusikColor.Contracts;

/// <summary>Параметры инициализации, которые хост передаёт плагину.</summary>
public sealed class VisualizerContext
{
    public int BandCount { get; }

    /// <summary>
    /// Центральные частоты (Гц) каждого логарифмического диапазона —
    /// та же сетка, что использует ядро для агрегации спектра. Нужна
    /// плагинам, которым важна не просто "громкость диапазона", а его
    /// реальная частота (например, для сопоставления с нотами).
    /// </summary>
    public float[] BandCenterFrequencies { get; }

    public VisualizerContext(int bandCount)
        : this(bandCount, ComputeLogSpacedCenterFrequencies(bandCount))
    {
    }

    public VisualizerContext(int bandCount, float[] bandCenterFrequencies)
    {
        BandCount = bandCount;
        BandCenterFrequencies = bandCenterFrequencies;
    }

    /// <summary>
    /// Считает центральные частоты диапазонов по той же логарифмической
    /// схеме, что и Core/Dsp/BandMapper, но независимо от частоты
    /// дискретизации — это позволяет вызвать её один раз при старте
    /// приложения, ещё до выбора источника звука.
    /// </summary>
    public static float[] ComputeLogSpacedCenterFrequencies(int bandCount, float minFreq = 30f, float maxFreq = 16000f)
    {
        var frequencies = new float[bandCount];
        double logMin = Math.Log(minFreq);
        double logMax = Math.Log(maxFreq);
        for (int i = 0; i < bandCount; i++)
        {
            double center = logMin + (logMax - logMin) * (i + 0.5) / bandCount;
            frequencies[i] = (float)Math.Exp(center);
        }
        return frequencies;
    }
}
