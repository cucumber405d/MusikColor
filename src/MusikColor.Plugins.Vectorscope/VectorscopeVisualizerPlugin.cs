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
/// Один фиксированный холодный бирюзовый фосфор — не мигает и не
/// меняется по времени (в отличие от "Осциллографа"), чтобы в наборе
/// визуализаций были разные механики цвета, а не только "всё мигает".
/// Масштаб по X и Y общий (не растягивается по осям отдельно) — иначе
/// форма фигур искажалась бы и не отражала бы реальное соотношение
/// каналов.
/// </summary>
public sealed class VectorscopeVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "vectorscope";
    public string DisplayName => "Вектороскоп (Лиссажу)";

    private static readonly SKColor PhosphorColor = new(40, 230, 200);

    private const float PeakDecayPerFrame = 0.985f;
    private const float MinRange = 0.02f;

    private float _runningPeak = MinRange;

    public void Init(VisualizerContext context)
    {
        _runningPeak = MinRange;
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

        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float radius = Math.Min(info.Width, info.Height) * 0.44f;

        DrawGraticule(canvas, centerX, centerY, radius);

        float frameMax = 0.0001f;
        for (int i = 0; i < n; i++)
        {
            frameMax = Math.Max(frameMax, Math.Abs(left[i]));
            frameMax = Math.Max(frameMax, Math.Abs(right[i]));
        }
        _runningPeak = Math.Max(frameMax, Math.Max(_runningPeak * PeakDecayPerFrame, MinRange));

        float scale = radius / _runningPeak;

        using var path = new SKPath();
        float lastX = centerX;
        float lastY = centerY;
        for (int i = 0; i < n; i++)
        {
            float x = centerX + Math.Clamp(left[i] * scale, -radius, radius);
            float y = centerY - Math.Clamp(right[i] * scale, -radius, radius);
            if (i == 0)
            {
                path.MoveTo(x, y);
            }
            else
            {
                path.LineTo(x, y);
            }
            lastX = x;
            lastY = y;
        }

        using (var glowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 6f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = PhosphorColor.WithAlpha(80),
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
            Color = PhosphorColor.WithAlpha(230),
        })
        {
            canvas.DrawPath(path, tracePaint);
        }

        // Яркая точка на самом свежем отсчёте — читается как "живая"
        // головка луча, а не просто застывший узор.
        using var headPaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 235) };
        canvas.DrawCircle(lastX, lastY, 3.2f, headPaint);
    }

    private static void DrawGraticule(SKCanvas canvas, float centerX, float centerY, float radius)
    {
        using var circlePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = PhosphorColor.WithAlpha(35),
        };
        using var axisPaint = new SKPaint
        {
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = PhosphorColor.WithAlpha(30),
        };
        using var diagPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = PhosphorColor.WithAlpha(20),
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
