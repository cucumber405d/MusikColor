using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.Vectorscope;

/// <summary>
/// "Вектороскоп" (фигуры Лиссажу) — левый канал по оси X, правый по
/// оси Y: точка бежит по экрану, а её след складывается в петляющие
/// узоры, форма которых зависит от разницы фаз и частот между
/// каналами. На моно-источнике левый и правый канал физически
/// совпадают, и след честно вытягивается в диагональную линию — это
/// не ошибка, а видно, что стерео нет.
///
/// Область между центром и кривой залита полупрозрачным цветом (как
/// заливка от оси в "Осциллографе", только здесь "ось" — это
/// центральная точка). Цвет луча раз в 5 секунд сам плавно перетекает
/// к новому случайному оттенку по всей палитре — тот же приём, что и в
/// "Частотных барах" и "Осциллографе".
///
/// Масштаб по X и Y общий (не растягивается по осям отдельно) — иначе
/// форма фигур искажалась бы и не отражала бы реальное соотношение
/// каналов.
/// </summary>
public sealed class VectorscopeVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "vectorscope";
    public string DisplayName => "Вектороскоп (Лиссажу)";

    private const float PeakDecayPerFrame = 0.985f;
    private const float MinRange = 0.02f;

    private static readonly TimeSpan ChangeInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(700);

    private readonly Random _random = new();

    private float _runningPeak = MinRange;
    private ColorState _color;

    private struct ColorState
    {
        public float FromHue;
        public float ToHue;
        public DateTime TransitionStart;
        public DateTime NextChangeAt;
    }

    public void Init(VisualizerContext context)
    {
        _runningPeak = MinRange;

        var now = DateTime.UtcNow;
        float initialHue = RandomHue();
        _color = new ColorState
        {
            FromHue = initialHue,
            ToHue = initialHue,
            TransitionStart = now,
            NextChangeAt = now + ChangeInterval,
        };
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(2, 4, 4));

        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        var left = frame.WaveformLeft;
        var right = frame.WaveformRight;
        int n = Math.Min(left.Length, right.Length);
        if (n < 2)
        {
            return;
        }

        var now = DateTime.UtcNow;
        float hue = AdvanceAndGetHue(now);
        var phosphorColor = SKColor.FromHsv(hue, 85, 95);

        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float radius = Math.Min(info.Width, info.Height) * 0.44f;

        DrawGraticule(canvas, centerX, centerY, radius, phosphorColor);

        float frameMax = 0.0001f;
        for (int i = 0; i < n; i++)
        {
            frameMax = Math.Max(frameMax, Math.Abs(left[i]));
            frameMax = Math.Max(frameMax, Math.Abs(right[i]));
        }
        _runningPeak = Math.Max(frameMax, Math.Max(_runningPeak * PeakDecayPerFrame, MinRange));

        float scale = radius / _runningPeak;

        var points = new SKPoint[n];
        for (int i = 0; i < n; i++)
        {
            float x = centerX + Math.Clamp(left[i] * scale, -radius, radius);
            float y = centerY - Math.Clamp(right[i] * scale, -radius, radius);
            points[i] = new SKPoint(x, y);
        }

        using var path = new SKPath();
        path.MoveTo(points[0]);
        for (int i = 1; i < n; i++)
        {
            path.LineTo(points[i]);
        }

        // Заливка от центра: веер из центральной точки через всю кривую
        // и обратно в центр — область между центром и следом закрашена
        // полупрозрачным цветом, как заливка от оси в "Осциллографе".
        using (var fillPath = new SKPath())
        {
            fillPath.MoveTo(centerX, centerY);
            for (int i = 0; i < n; i++)
            {
                fillPath.LineTo(points[i]);
            }
            fillPath.Close();

            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = phosphorColor.WithAlpha(55),
            };
            canvas.DrawPath(fillPath, fillPaint);
        }

        using (var glowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 6f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = phosphorColor.WithAlpha(80),
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(5f, 5f),
        })
        {
            canvas.DrawPath(path, glowPaint);
        }

        using (var tracePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.8f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = phosphorColor.WithAlpha(230),
        })
        {
            canvas.DrawPath(path, tracePaint);
        }

        // Яркая точка на самом свежем отсчёте — читается как "живая"
        // головка луча, а не просто застывший узор.
        using var headPaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 235) };
        canvas.DrawCircle(points[n - 1], 3.2f, headPaint);
    }

    private float AdvanceAndGetHue(DateTime now)
    {
        if (now >= _color.NextChangeAt)
        {
            _color.FromHue = InterpolateHue(_color, now);
            _color.ToHue = RandomHue();
            _color.TransitionStart = now;
            _color.NextChangeAt = now + ChangeInterval;
        }

        return InterpolateHue(_color, now);
    }

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

    private static void DrawGraticule(SKCanvas canvas, float centerX, float centerY, float radius, SKColor color)
    {
        using var circlePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = color.WithAlpha(35),
        };
        using var axisPaint = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = color.WithAlpha(30),
        };
        using var diagPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = color.WithAlpha(20),
        };

        canvas.DrawCircle(centerX, centerY, radius, circlePaint);
        canvas.DrawCircle(centerX, centerY, radius * 0.5f, circlePaint);

        canvas.DrawLine(centerX - radius, centerY, centerX + radius, centerY, axisPaint);
        canvas.DrawLine(centerX, centerY - radius, centerX, centerY + radius, axisPaint);

        float diag = radius * 0.9f;
        canvas.DrawLine(centerX - diag, centerY - diag, centerX + diag, centerY + diag, diagPaint);
        canvas.DrawLine(centerX - diag, centerY + diag, centerX + diag, centerY - diag, diagPaint);
    }
}
