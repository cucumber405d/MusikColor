using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.ColorField;

/// <summary>
/// "Цветомузыка" в духе советских самодельных наборов на транзисторах:
/// три канала реагируют каждый на свой участок спектра —
/// бас → красный, середина → жёлтый, верха → зелёный.
/// Синий — не "ещё один цвет", а сама тишина: база, из которой цветные
/// каналы отъедают долю по мере появления сигнала в соответствующей
/// полосе. Экран не заливается одним цветом: горит поле "лампочек"
/// в случайных точках, каждая раз в несколько секунд перепрыгивает на
/// новое место — отсюда мерцающая, а не статичная картинка. Суммарная
/// "светимость" (число зажжённых точек) всегда одна и та же — меняется
/// только цветовой состав.
/// </summary>
public sealed class ColorFieldVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "color-field";
    public string DisplayName => "Цветомузыка";

    private const int LampCount = 220;

    private static readonly SKColor BassColor = new(230, 40, 40);     // бас — красный
    private static readonly SKColor MidColor = new(240, 200, 30);     // середина — жёлтый
    private static readonly SKColor TrebleColor = new(50, 220, 90);   // верха — зелёный
    private static readonly SKColor SilenceColor = new(30, 70, 230);  // тишина — синий

    private readonly Random _random = new();
    private Lamp[] _lamps = Array.Empty<Lamp>();
    private int _frame;

    // Сглаженные доли по трём каналам — чтобы состав лампочек не дёргался
    // кадр от кадра, а плавно "перетекал" из одного цвета в другой.
    private float _bassShare;
    private float _midShare;
    private float _trebleShare;

    private struct Lamp
    {
        public float X;
        public float Y;
        public float Radius;
        public int ReshuffleAt;
    }

    public void Init(VisualizerContext context)
    {
        _lamps = new Lamp[LampCount];
        for (int i = 0; i < _lamps.Length; i++)
        {
            _lamps[i] = NewLamp(i);
        }
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(4, 4, 10));

        if (_lamps.Length == 0 || info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        _frame++;

        var (bass, mid, treble) = SplitBands(frame.Bands);

        // Нормализуем в доли одного общего "бюджета светимости": если
        // энергии в сумме мало (тишина) — всё остаётся синим; появилась
        // энергия в полосе — соответствующая доля лампочек зажигается
        // её цветом, но общее число лампочек не меняется.
        float total = bass + mid + treble;
        float targetBass, targetMid, targetTreble;
        if (total < 0.02f)
        {
            targetBass = targetMid = targetTreble = 0f;
        }
        else
        {
            float scale = Math.Min(1f, total) / total;
            targetBass = bass * scale;
            targetMid = mid * scale;
            targetTreble = treble * scale;
        }

        const float smoothing = 0.25f;
        _bassShare += (targetBass - _bassShare) * smoothing;
        _midShare += (targetMid - _midShare) * smoothing;
        _trebleShare += (targetTreble - _trebleShare) * smoothing;

        int bassLamps = (int)(LampCount * _bassShare);
        int midLamps = (int)(LampCount * _midShare);
        int trebleLamps = (int)(LampCount * _trebleShare);
        // Остаток (LampCount - зажжённые цветом) — синие, "тишина".

        using var paint = new SKPaint { IsAntialias = true };

        for (int i = 0; i < _lamps.Length; i++)
        {
            ref var lamp = ref _lamps[i];

            if (_frame >= lamp.ReshuffleAt)
            {
                lamp = NewLamp(i);
            }

            SKColor color;
            if (i < bassLamps)
            {
                color = BassColor;
            }
            else if (i < bassLamps + midLamps)
            {
                color = MidColor;
            }
            else if (i < bassLamps + midLamps + trebleLamps)
            {
                color = TrebleColor;
            }
            else
            {
                color = SilenceColor;
            }

            paint.Color = color;
            canvas.DrawCircle(lamp.X * info.Width, lamp.Y * info.Height, lamp.Radius, paint);
        }
    }

    private Lamp NewLamp(int seedOffset)
    {
        return new Lamp
        {
            X = (float)_random.NextDouble(),
            Y = (float)_random.NextDouble(),
            Radius = 3f + (float)_random.NextDouble() * 6f,
            ReshuffleAt = _frame + 90 + _random.Next(0, 180) + seedOffset % 30,
        };
    }

    private static (float bass, float mid, float treble) SplitBands(float[] bands)
    {
        int n = bands.Length;
        if (n == 0)
        {
            return (0f, 0f, 0f);
        }

        // При логарифмическом распределении полос, которое строит
        // BandMapper в Core (30 Гц..16 кГц), деление по трети индексов
        // примерно соответствует классическим кроссоверам ~250 Гц и ~2 кГц
        // из аналоговых цветомузыкальных схем.
        int thirdA = Math.Max(1, n / 3);
        int thirdB = Math.Max(thirdA + 1, n * 2 / 3);

        float bass = Average(bands, 0, thirdA);
        float mid = Average(bands, thirdA, thirdB);
        float treble = Average(bands, thirdB, n);
        return (bass, mid, treble);
    }

    private static float Average(float[] values, int start, int end)
    {
        float sum = 0f;
        int count = 0;
        for (int i = start; i < end && i < values.Length; i++)
        {
            sum += values[i];
            count++;
        }
        return count > 0 ? sum / count : 0f;
    }
}
