using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.Oscilloscope;

/// <summary>
/// "Осциллограф" — классическая ламповая ЭЛТ-развёртка: реальная форма
/// звуковой волны (не частотный спектр) бежит одной светящейся линией
/// поперёк экрана, поверх тусклой сетки-графика, со сканлайнами для
/// аутентичности. Янтарный фосфор — единственный плагин с таким цветом
/// в проекте, чтобы не путать с зелёным каналом "Цветомузыки" или любой
/// другой палитрой.
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

    private static readonly SKColor PhosphorColor = new(255, 170, 40);

    private const float PeakDecayPerFrame = 0.985f;
    private const float MinRange = 0.02f;

    private float _runningPeak = MinRange;

    public void Init(VisualizerContext context)
    {
        _runningPeak = MinRange;
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(4, 3, 2));

        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        var wave = frame.Waveform;
        if (wave.Length == 0)
        {
            return;
        }

        DrawGraticule(canvas, info);

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

        using var tracePath = new SKPath();
        float stepX = (float)info.Width / (wave.Length - 1);
        for (int i = 0; i < wave.Length; i++)
        {
            float x = i * stepX;
            float y = centerY - Math.Clamp(wave[i] * gain, -halfHeight, halfHeight);
            if (i == 0)
            {
                tracePath.MoveTo(x, y);
            }
            else
            {
                tracePath.LineTo(x, y);
            }
        }

        using (var glowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 7f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = PhosphorColor.WithAlpha(90),
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
            Color = PhosphorColor,
        })
        {
            canvas.DrawPath(tracePath, tracePaint);
        }

        DrawScanlines(canvas, info);
    }

    private static void DrawGraticule(SKCanvas canvas, SKImageInfo info)
    {
        using var gridPaint = new SKPaint
        {
            IsAntialias = false,
            Color = PhosphorColor.WithAlpha(22),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
        };
        using var axisPaint = new SKPaint
        {
            IsAntialias = false,
            Color = PhosphorColor.WithAlpha(45),
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
