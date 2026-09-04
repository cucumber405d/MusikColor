using System;

namespace MusikColor.Contracts;

/// <summary>
/// Нормализованный кадр частотного анализа — то, что ядро отдаёт
/// визуализации. Bands уже приведены к диапазону [0..1] и сглажены.
/// </summary>
public sealed class FrequencyFrame
{
    /// <summary>Уровень по каждой полосе частот, [0..1], лог-шкала по частоте.</summary>
    public float[] Bands { get; }

    /// <summary>Общая громкость кадра, [0..1].</summary>
    public float Volume { get; }

    /// <summary>Признак резкого всплеска громкости (упрощённый бит-детект).</summary>
    public bool Beat { get; }

    public DateTime Timestamp { get; }

    public FrequencyFrame(float[] bands, float volume, bool beat, DateTime timestamp)
    {
        Bands = bands;
        Volume = volume;
        Beat = beat;
        Timestamp = timestamp;
    }
}
