using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.MirrorSpectrum;

/// <summary>
/// "Зеркальный спектр" — классический вид анализатора спектра: полосы
/// растут вверх от горизонтальной линии слева направо (бас -> верха),
/// а под этой линией — их приглушённое, размытое отражение, как в
/// стекле или на глянцевом танцполе.
///
/// Цвет столбцов — как в "Частотных барах": не завязан на частоту,
/// каждый бар раз в 5 секунд сам выбирает случайный оттенок по всей
/// палитре и плавно перетекает к нему, со своим случайным сдвигом фазы,
/// чтобы вся картина не переключалась разом одним щелчком.
/// </summary>
public sealed class MirrorSpectrumVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "mirror-spectrum";
    public string DisplayName => "Зеркальный спектр";

    private const float AttackSmoothing = 0.55f;
    private const float ReleaseSmoothing = 0.09f;

    private static readonly TimeSpan ChangeInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(600);

    private readonly Random _random = new();

    private float[] _smoothed = Array.Empty<float>();
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
        _smoothed = new float[Math.Max(1, context.BandCount)];
        _colors = Array.Empty<ColorState>(); // достроится лениво в Render под реальный bandCount
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(4, 4, 10));

        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        var bands = frame.Bands;
        int n = bands.Length;
        if (n == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (_smoothed.Length != n)
        {
            _smoothed = new float[n];
        }

        if (_colors.Length != n)
        {
            _colors = new ColorState[n];
            for (int i = 0; i < n; i++)
            {
                float initialHue = RandomHue();
                _colors[i] = new ColorState
                {
                    FromHue = initialHue,
                    ToHue = initialHue,
                    TransitionStart = now,
                    NextChangeAt = now + TimeSpan.FromMilliseconds(_random.NextDouble() * ChangeInterval.TotalMilliseconds),
                };
            }
        }

        for (int i = 0; i < n; i++)
        {
            float target = Math.Clamp(bands[i], 0f, 1f);
            float smoothing = target > _smoothed[i] ? AttackSmoothing : ReleaseSmoothing;
            _smoothed[i] += (target - _smoothed[i]) * smoothing;
        }

        float baseline = info.Height * 0.58f;
        float availableUp = baseline;
        float availableDown = info.Height - baseline;
        float maxBarHeight = Math.Min(availableUp, availableDown) * 0.92f;

        float slot = (float)info.Width / n;
        float barWidth = Math.Max(1.5f, slot * 0.72f);
        float gap = (slot - barWidth) / 2f;

        // Отражение рисуем первым слоем — приглушённое и размытое,
        // чтобы основные полосы легли поверх него чётко и контрастно.
        using (var reflectionPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            ImageFilter = SKImageFilter.CreateBlur(2.5f, 2.5f),
        })
        {
            for (int i = 0; i < n; i++)
            {
                float level = _smoothed[i];
                if (level <= 0.005f)
                {
                    continue;
                }

                float height = level * maxBarHeight;
                float x = i * slot + gap;
                float hue = AdvanceAndGetHue(i, now);
                var color = SKColor.FromHsv(hue, 85, 95);

                reflectionPaint.Color = color.WithAlpha((byte)Math.Clamp(30f + level * 90f, 30f, 120f));
                canvas.DrawRect(new SKRect(x, baseline, x + barWidth, baseline + height), reflectionPaint);
            }
        }

        using (var barPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
        using (var capPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(255, 255, 255, 200) })
        {
            for (int i = 0; i < n; i++)
            {
                float level = _smoothed[i];
                if (level <= 0.005f)
                {
                    continue;
                }

                float height = level * maxBarHeight;
                float x = i * slot + gap;
                float top = baseline - height;
                float hue = GetCurrentHue(i, now);
                var color = SKColor.FromHsv(hue, 85, 95);

                barPaint.Color = color.WithAlpha((byte)Math.Clamp(180f + level * 75f, 180f, 255f));
                canvas.DrawRect(new SKRect(x, top, x + barWidth, baseline), barPaint);

                // Тонкая светлая шапочка на верхушке — читается как блик,
                // подчёркивает высоту полосы.
                canvas.DrawRect(new SKRect(x, top, x + barWidth, top + Math.Min(2.5f, height)), capPaint);
            }
        }

        // Линия горизонта — тонкая, полупрозрачная, чтобы обозначить
        // границу "пола", от которого отражаются полосы.
        using var horizonPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 40),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
        };
        canvas.DrawLine(0, baseline, info.Width, baseline, horizonPaint);
    }

    /// <summary>
    /// Если для бара i подошло время смены — фиксирует текущий оттенок как
    /// новую точку старта и выбирает новую случайную цель. Возвращает
    /// оттенок, который нужно рисовать прямо сейчас.
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

    /// <summary>
    /// Отражение рисуется раньше основного бара в том же кадре, поэтому
    /// продвигать состояние цвета (менять хеш) должен только один из двух
    /// проходов — иначе смена цвета сработает дважды за кадр. Отражение
    /// вызывает AdvanceAndGetHue (продвигает), основной бар — этот метод
    /// (только читает уже продвинутое состояние).
    /// </summary>
    private float GetCurrentHue(int index, DateTime now) => InterpolateHue(_colors[index], now);

    private static float InterpolateHue(in ColorState state, DateTime now)
    {
        double elapsed = (now - state.TransitionStart).TotalMilliseconds;
        float t = (float)Math.Clamp(elapsed / TransitionDuration.TotalMilliseconds, 0.0, 1.0);
        return LerpHue(state.FromHue, state.ToHue, t);
    }

    private static float LerpHue(float from, float to, float t)
    {
        float diff = to - from;
        diff = ((diff + 540f) % 360f) - 180f;
        float result = from + diff * t;
        return ((result % 360f) + 360f) % 360f;
    }

    private float RandomHue() => (float)(_random.NextDouble() * 360.0);
}
