using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.Bars;

/// <summary>
/// Классический "блочный" эквалайзер — как в железных LED-панелях и
/// старых Winamp-визуализациях: каждая полоса рисуется не сплошным
/// прямоугольником, а стопкой отдельных светящихся сегментов с зазорами,
/// плюс мягкое свечение под ними. Сверху — "пиковый" сегмент, который
/// держится и медленно опадает, как индикатор пика на настоящих
/// LED-эквалайзерах.
///
/// Цвет столбца не завязан на частоту — каждый бар раз в 5 секунд сам
/// выбирает случайный оттенок по всей палитре и плавно (не рывком)
/// перетекает к нему. Смена по каждому бару стартует со своим случайным
/// сдвигом по фазе, чтобы вся панель не переключалась разом одним щелчком.
/// </summary>
public sealed class BarsVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "bars";
    public string DisplayName => "Частотные бары";

    private const int SegmentCount = 24;
    private const float SegmentGap = 3f;
    private const float ColumnGap = 3f;
    private const float PeakHoldFrames = 30f;
    private const float PeakFallSpeed = 0.01f;

    private static readonly TimeSpan ChangeInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(600);

    private readonly Random _random = new();

    private float[] _peak = Array.Empty<float>();
    private float[] _peakHoldTimer = Array.Empty<float>();
    private ColorState[] _colors = Array.Empty<ColorState>();

    private struct ColorState
    {
        public float FromHue;
        public float ToHue;
        public DateTime TransitionStart;
        public DateTime NextChangeAt;
    }

    public void Init(VisualizerContext context)
    {
        _peak = new float[context.BandCount];
        _peakHoldTimer = new float[context.BandCount];
        _colors = Array.Empty<ColorState>(); // достроится лениво в Render под реальный bandCount
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(6, 6, 12));

        int bandCount = frame.Bands.Length;
        if (bandCount == 0 || info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (_peak.Length != bandCount)
        {
            _peak = new float[bandCount];
            _peakHoldTimer = new float[bandCount];
        }

        if (_colors.Length != bandCount)
        {
            _colors = new ColorState[bandCount];
            for (int i = 0; i < bandCount; i++)
            {
                float initialHue = RandomHue();
                _colors[i] = new ColorState
                {
                    FromHue = initialHue,
                    ToHue = initialHue,
                    TransitionStart = now,
                    // Случайный сдвиг фазы в пределах интервала — иначе все
                    // бары синхронно щёлкнут цветом одновременно.
                    NextChangeAt = now + TimeSpan.FromMilliseconds(_random.NextDouble() * ChangeInterval.TotalMilliseconds),
                };
            }
        }

        float slotWidth = (float)info.Width / bandCount;
        float columnWidth = Math.Max(1f, slotWidth - ColumnGap);
        float pitch = (float)info.Height / SegmentCount;
        float segmentHeight = Math.Max(1f, pitch - SegmentGap);

        using var paint = new SKPaint { IsAntialias = true };
        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(8f, 8f),
        };

        for (int i = 0; i < bandCount; i++)
        {
            float value = Math.Clamp(frame.Bands[i], 0f, 1f);
            UpdatePeak(i, value);

            float hue = AdvanceAndGetHue(i, now);
            var litColor = SKColor.FromHsv(hue, 85, 95);
            var dimColor = SKColor.FromHsv(hue, 55, 12);

            float x = i * slotWidth + ColumnGap / 2f;

            // Мягкое свечение под всей "зажжённой" частью столбца — рисуем
            // один раз на весь столбец, а не на каждый сегмент отдельно,
            // иначе блюр-фильтр слишком дорог при 48 полосах на 60 fps.
            if (value > 0.01f)
            {
                float glowTop = info.Height - value * info.Height;
                glowPaint.Color = litColor.WithAlpha(110);
                canvas.DrawRect(new SKRect(x, glowTop, x + columnWidth, info.Height), glowPaint);
            }

            int litSegments = (int)(value * SegmentCount);
            int peakSegment = Math.Clamp((int)(_peak[i] * SegmentCount), 0, SegmentCount - 1);

            for (int s = 0; s < SegmentCount; s++)
            {
                float yBottom = info.Height - s * pitch;
                float yTop = yBottom - segmentHeight;
                var rect = new SKRect(x, yTop, x + columnWidth, yBottom);

                bool isLit = s < litSegments;
                bool isPeak = s == peakSegment && peakSegment >= litSegments;

                paint.Color = isPeak ? new SKColor(255, 255, 255, 235) : (isLit ? litColor : dimColor);
                canvas.DrawRoundRect(rect, 2f, 2f, paint);
            }
        }

        if (frame.Beat)
        {
            using var flashPaint = new SKPaint { Color = new SKColor(255, 255, 255, 22) };
            canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), flashPaint);
        }
    }

    /// <summary>
    /// Если для бара i подошло время смены — фиксирует текущий (уже
    /// проинтерполированный) оттенок как новую точку старта и выбирает
    /// новую случайную цель. Возвращает оттенок, который нужно рисовать
    /// прямо сейчас (может быть ещё в процессе плавного перехода).
    /// </summary>
    private float AdvanceAndGetHue(int index, DateTime now)
    {
        ref var state = ref _colors[index];

        if (now >= state.NextChangeAt)
        {
            state.FromHue = InterpolateHue(state, now);
            state.ToHue = RandomHue();
            state.TransitionStart = now;
            state.NextChangeAt = now + ChangeInterval;
        }

        return InterpolateHue(state, now);
    }

    private static float InterpolateHue(in ColorState state, DateTime now)
    {
        double elapsed = (now - state.TransitionStart).TotalMilliseconds;
        float t = (float)Math.Clamp(elapsed / TransitionDuration.TotalMilliseconds, 0.0, 1.0);
        return LerpHue(state.FromHue, state.ToHue, t);
    }

    private static float LerpHue(float from, float to, float t)
    {
        // Кратчайший путь по кругу — иначе переход иногда шёл бы в обход
        // через всю палитру вместо прямого пути.
        float diff = to - from;
        diff = ((diff + 540f) % 360f) - 180f;
        float result = from + diff * t;
        return ((result % 360f) + 360f) % 360f;
    }

    private float RandomHue() => (float)(_random.NextDouble() * 360.0);

    private void UpdatePeak(int index, float value)
    {
        if (value >= _peak[index])
        {
            _peak[index] = value;
            _peakHoldTimer[index] = PeakHoldFrames;
        }
        else if (_peakHoldTimer[index] > 0f)
        {
            _peakHoldTimer[index] -= 1f;
        }
        else
        {
            _peak[index] = Math.Max(value, _peak[index] - PeakFallSpeed);
        }
    }
}
