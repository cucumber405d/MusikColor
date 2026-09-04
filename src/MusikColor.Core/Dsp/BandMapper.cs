using System;

namespace MusikColor.Core.Dsp;

/// <summary>
/// Группирует линейные бины FFT в логарифмические полосы частот —
/// именно так устроены классические частотные бары: одинаковая ширина
/// столбика соответствует не равному числу герц, а равному числу "октав".
/// </summary>
internal sealed class BandMapper
{
    private readonly int[] _binStart;
    private readonly int[] _binEnd;

    public int BandCount { get; }

    public BandMapper(int bandCount, int fftSize, int sampleRate, float minFreq = 30f, float maxFreq = 16000f)
    {
        BandCount = bandCount;
        _binStart = new int[bandCount];
        _binEnd = new int[bandCount];

        int usableBins = fftSize / 2;
        float nyquist = sampleRate / 2f;
        maxFreq = Math.Min(maxFreq, nyquist - 1f);

        double logMin = Math.Log(minFreq);
        double logMax = Math.Log(maxFreq);

        for (int b = 0; b < bandCount; b++)
        {
            double loF = Math.Exp(logMin + (logMax - logMin) * b / bandCount);
            double hiF = Math.Exp(logMin + (logMax - logMin) * (b + 1) / bandCount);

            int loBin = (int)(loF / nyquist * usableBins);
            int hiBin = (int)(hiF / nyquist * usableBins);

            loBin = Math.Clamp(loBin, 0, usableBins - 1);
            hiBin = Math.Clamp(Math.Max(hiBin, loBin + 1), 0, usableBins);

            _binStart[b] = loBin;
            _binEnd[b] = hiBin;
        }
    }

    public void Map(ReadOnlySpan<float> magnitudes, Span<float> bandsOut)
    {
        for (int b = 0; b < BandCount; b++)
        {
            int lo = _binStart[b];
            int hi = _binEnd[b];
            float sum = 0f;
            int count = 0;

            for (int i = lo; i < hi && i < magnitudes.Length; i++)
            {
                sum += magnitudes[i];
                count++;
            }

            bandsOut[b] = count > 0 ? sum / count : 0f;
        }
    }
}
