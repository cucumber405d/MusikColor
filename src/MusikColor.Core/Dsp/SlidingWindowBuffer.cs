using System;

namespace MusikColor.Core.Dsp;

/// <summary>
/// Скользящее окно фиксированной длины: хранит последние N сэмплов.
/// Аудио приходит чанками произвольного размера — буфер сам сдвигает
/// старые данные и дописывает новые, так что снаружи всегда доступно
/// последнее окно нужной длины для FFT.
/// </summary>
internal sealed class SlidingWindowBuffer
{
    private readonly float[] _buffer;
    private int _filled;

    public bool IsFull => _filled >= _buffer.Length;

    public SlidingWindowBuffer(int size)
    {
        _buffer = new float[size];
        _filled = 0;
    }

    public void Push(ReadOnlySpan<float> samples)
    {
        int n = samples.Length;
        int size = _buffer.Length;

        if (n <= 0)
        {
            return;
        }

        if (n >= size)
        {
            samples.Slice(n - size).CopyTo(_buffer);
            _filled = size;
            return;
        }

        Array.Copy(_buffer, n, _buffer, 0, size - n);
        samples.CopyTo(_buffer.AsSpan(size - n));
        _filled = Math.Min(size, _filled + n);
    }

    public ReadOnlySpan<float> Snapshot() => _buffer;
}
