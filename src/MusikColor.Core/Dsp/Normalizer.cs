using System;

namespace MusikColor.Core.Dsp;

/// <summary>
/// Приводит "сырые" амплитуды полос к диапазону [0..1]:
///  1) переводим в децибелы и обрезаем снизу (floorDb) — иначе тишина
///     даёт хаотичный шум на графике;
///  2) на каждую полосу отдельно отслеживаем скользящий пик (простое
///     авто-усиление, AGC) — тихая и громкая композиция одинаково хорошо
///     используют весь диапазон визуализации;
///  3) сглаживаем во времени с разной скоростью роста/спада, чтобы бары
///     резко подскакивали, но плавно "опадали", как в старых эквалайзерах.
/// </summary>
internal sealed class Normalizer
{
    private readonly float[] _peak;
    private readonly float[] _smoothed;
    private readonly float _peakDecay;
    private readonly float _riseSpeed;
    private readonly float _fallSpeed;
    private readonly float _floorDb;

    public Normalizer(int bandCount, float peakDecay = 0.992f, float riseSpeed = 0.6f, float fallSpeed = 0.12f, float floorDb = -60f)
    {
        _peak = new float[bandCount];
        _smoothed = new float[bandCount];
        _peakDecay = peakDecay;
        _riseSpeed = riseSpeed;
        _fallSpeed = fallSpeed;
        _floorDb = floorDb;
        Array.Fill(_peak, 1e-3f);
    }

    public void Apply(Span<float> bands)
    {
        for (int i = 0; i < bands.Length; i++)
        {
            float mag = Math.Max(bands[i], 1e-6f);
            float db = 20f * MathF.Log10(mag);
            float norm = Math.Clamp((db - _floorDb) / -_floorDb, 0f, 1f);

            _peak[i] = MathF.Max(norm, _peak[i] * _peakDecay);
            float scaled = _peak[i] > 0.001f ? norm / _peak[i] : norm;
            scaled = Math.Clamp(scaled, 0f, 1f);

            float speed = scaled > _smoothed[i] ? _riseSpeed : _fallSpeed;
            _smoothed[i] += (scaled - _smoothed[i]) * speed;

            bands[i] = _smoothed[i];
        }
    }
}
