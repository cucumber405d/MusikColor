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
    private readonly float[] _gain;

    public int BandCount { get; }

    public BandMapper(int bandCount, int fftSize, int sampleRate, float minFreq = 30f, float maxFreq = 16000f)
    {
        BandCount = bandCount;
        _binStart = new int[bandCount];
        _binEnd = new int[bandCount];
        _gain = new float[bandCount];

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

            // Компенсация естественного спада энергии музыки к верхним
            // частотам: без неё правая часть экрана почти всегда "мёртвая"
            // даже с адаптивной нормализацией на канал — амплитуда там
            // систематически ниже, а не просто "тише сейчас". Подобрано
            // экспериментально, не претендует на акустическую точность.
            _gain[b] = 1f + 3.5f * b / Math.Max(1, bandCount - 1);
        }
    }

    public void Map(ReadOnlySpan<float> magnitudes, Span<float> bandsOut)
    {
        for (int b = 0; b < BandCount; b++)
        {
            int lo = _binStart[b];
            int hi = _binEnd[b];

            // Пик, а не среднее: при логарифмическом разбиении верхние
            // полосы охватывают сотни бинов, и резкий узкополосный всплеск
            // (тарелки, "s"-звуки) тонет в среднем по огромному числу
            // соседних тихих бинов, если брать среднее.
            float peak = 0f;
            for (int i = lo; i < hi && i < magnitudes.Length; i++)
            {
                if (magnitudes[i] > peak)
                {
                    peak = magnitudes[i];
                }
            }

            bandsOut[b] = peak * _gain[b];
        }
    }
}
