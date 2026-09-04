using System;

namespace MusikColor.Core.Dsp;

/// <summary>
/// Минимальное быстрое преобразование Фурье (radix-2 Cooley-Tukey, in-place,
/// decimation-in-time). Длина массивов должна быть степенью двойки.
/// Специально не тянем сюда внешнюю DSP-библиотеку — это ~50 строк
/// хорошо изученного алгоритма, ядро остаётся без лишних зависимостей.
/// </summary>
internal static class Fft
{
    public static void Forward(float[] re, float[] im)
    {
        int n = re.Length;
        if (n != im.Length || n == 0 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("Длина re/im должна совпадать и быть степенью двойки.");
        }

        // Перестановка по битовому реверсу.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j &= ~bit;
            }
            j |= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        // Итеративные "бабочки" по возрастающим блокам длины 2, 4, 8, ...
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            float wRe = (float)Math.Cos(angle);
            float wIm = (float)Math.Sin(angle);
            int half = len >> 1;

            for (int i = 0; i < n; i += len)
            {
                float curRe = 1f;
                float curIm = 0f;

                for (int k = 0; k < half; k++)
                {
                    int a = i + k;
                    int b = i + k + half;

                    float uRe = re[a];
                    float uIm = im[a];
                    float vRe = re[b] * curRe - im[b] * curIm;
                    float vIm = re[b] * curIm + im[b] * curRe;

                    re[a] = uRe + vRe;
                    im[a] = uIm + vIm;
                    re[b] = uRe - vRe;
                    im[b] = uIm - vIm;

                    float nextRe = curRe * wRe - curIm * wIm;
                    float nextIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                    curIm = nextIm;
                }
            }
        }
    }
}
