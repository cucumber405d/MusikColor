using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.Oscilloscope;

/// <summary>
/// "Осциллограф" — классическая ЭЛТ-развёртка: реальная форма звуковой
/// волны (не частотный спектр) бежит поперёк экрана, залитая от средней
/// оси (как в аудиоредакторах), поверх тусклой сетки-графика, со
/// сканлайнами для аутентичности.
///
/// Цвет луча не статичный — раз в 5 секунд сам плавно перетекает к
/// новому случайному оттенку по всей палитре (тот же приём, что и в
/// "Частотных барах"), так что при долгом просмотре картинка не
/// приедается одним и тем же янтарным светом.
///
/// Вертикальный масштаб калибруется по входящему сигналу самостоятельно
/// (тот же приём, что и в "Спектрограмме-водопад"): недавний пиковый
/// размах растёт мгновенно и медленно спадает, так что волна всегда
/// использует большую часть экрана, а не превращается в плоскую линию
/// на тихой записи.
/// </summary>
public sealed class OscilloscopeVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "oscilloscope";
    public string DisplayName => "Осциллограф";

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
        canvas.Clear(new SKColor(4, 3, 2));

        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        var wave = frame.Waveform;
        if (wave.Length < 2)
        {
            return;
        }

        var now = DateTime.UtcNow;
        float hue = AdvanceAndGetHue(now);
        var phosphorColor = SKColor.FromHsv(hue, 85, 95);

        DrawGraticule(canvas, info, phosphorColor);

        float frameMax = 0f;
        for (int i = 0; i < wave.Length; i++)
        {
            float abs = MathF.Abs(wave[i]);
            if (abs > frameMax)
            {
                frameMax = abs;
            }
        }
        _runningPeak = Math.Max(frameMax, Math.Max(_runningPeak * PeakDecayPerFrame, MinRange));

        float centerY = info.Height / 2f;
        float halfHeight = info.Height * 0.42f;
        float gain = halfHeight / _runningPeak;

        float stepX = (float)info.Width / (wave.Length - 1);

        var points = new SKPoint[wave.Length];
        for (int i = 0; i < wave.Length; i++)
        {
            float x = i * stepX;
            float y = centerY - Math.Clamp(wave[i] * gain, -halfHeight, halfHeight);
            points[i] = new SKPoint(x, y);
        }

        using var tracePath = new SKPath();
        tracePath.MoveTo(points[0]);
        for (int i = 1; i < points.Length; i++)
        {
            tracePath.LineTo(points[i]);
        }

        // Заливка от средней оси: контур идёт по оси до первой точки
        // волны, дальше по самой волне, затем снова на ось у правого
        // края и замыкается прямой линией по оси — так получаются
        // залитые "лепестки" и выше, и ниже оси, как в аудиоредакторах,
        // а не просто тонкая линия.
        using (var fillPath = new SKPath())
        {
            fillPath.MoveTo(0, centerY);
            fillPath.LineTo(points[0]);
            for (int i = 1; i < points.Length; i++)
            {
                fillPath.LineTo(points[i]);
            }
            fillPath.LineTo(info.Width, centerY);
            fillPath.Close();

            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = phosphorColor.WithAlpha(60),
            };
            canvas.DrawPath(fillPath, fillPaint);
        }

        using (var glowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 7f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = phosphorColor.WithAlpha(90),
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(6f, 6f),
        })
        {
            canvas.DrawPath(tracePath, glowPaint);
        }

        using (var tracePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.2f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = phosphorColor,
        })
        {
            canvas.DrawPath(tracePath, tracePaint);
        }

        DrawScanlines(canvas, info);
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

    private static void DrawGraticule(SKCanvas canvas, SKImageInfo info, SKColor color)
    {
        using var gridPaint = new SKPaint
        {
            IsAntialias = false,
            Color = color.WithAlpha(22),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
        };
        using var axisPaint = new SKPaint
        {
            IsAntialias = false,
            Color = color.WithAlpha(45),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
        };

        const int columns = 10;
        const int rows = 8;

        for (int c = 1; c < columns; c++)
        {
            float x = info.Width * c / (float)columns;
            canvas.DrawLine(x, 0, x, info.Height, gridPaint);
        }

        for (int r = 1; r < rows; r++)
        {
            float y = info.Height * r / (float)rows;
            canvas.DrawLine(0, y, info.Width, y, gridPaint);
        }

        canvas.DrawLine(info.Width / 2f, 0, info.Width / 2f, info.Height, axisPaint);
        canvas.DrawLine(0, info.Height / 2f, info.Width, info.Height / 2f, axisPaint);
    }

    private static void DrawScanlines(SKCanvas canvas, SKImageInfo info)
    {
        using var scanPaint = new SKPaint
        {
            IsAntialias = false,
            Color = new SKColor(0, 0, 0, 26),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
        };

        for (float y = 0; y < info.Height; y += 4f)
        {
            canvas.DrawLine(0, y, info.Width, y, scanPaint);
        }
    }
}
