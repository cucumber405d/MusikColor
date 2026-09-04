using System;

namespace MusikColor.Core.Dsp;

internal static class WindowFunctions
{
    /// <summary>Окно Ханна — сглаживает края анализируемого куска сигнала перед FFT.</summary>
    public static float[] Hann(int size)
    {
        var window = new float[size];
        for (int i = 0; i < size; i++)
        {
            window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (size - 1)));
        }
        return window;
    }
}
