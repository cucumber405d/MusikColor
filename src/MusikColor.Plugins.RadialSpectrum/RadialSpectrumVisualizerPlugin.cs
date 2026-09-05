using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.RadialSpectrum;

/// <summary>
/// "Круговой спектр" — классика VJ-визуализации: частотные полосы
/// расходятся лучами из центра экрана по кругу, весь узор медленно
/// вращается, а на басах вращение ускоряется и в центре пульсирует
/// светлое пятно. Цвет луча — радуга по углу (позиция полосы на
/// круге), яркость и насыщенность растут вместе с громкостью полосы.
/// </summary>
public sealed class RadialSpectrumVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "radial-spectrum";
    public string DisplayName => "Круговой спектр";

    // Атака быстрая (луч мгновенно откликается на удар), спад медленный
    // (плавно гаснет) — тот же приём, что и в остальных плагинах.
    private const float AttackSmoothing = 0.5f;
    private const float ReleaseSmoothing = 0.08f;

    private const float BaseRotationSpeedDegPerSec = 4f;
    private const float BassRotationBoostDegPerSec = 70f;

    private float[] _smoothed = Array.Empty<float>();
    private float _rotationDegrees;
    private DateTime _lastFrameTime = DateTime.UtcNow;

    public void Init(VisualizerContext context)
    {
        _smoothed = new float[Math.Max(1, context.BandCount)];
        _rotationDegrees = 0f;
        _lastFrameTime = DateTime.UtcNow;
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(4, 4, 10));

        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        float dt = Math.Clamp((float)(now - _lastFrameTime).TotalSeconds, 0f, 0.25f);
        _lastFrameTime = now;

        var bands = frame.Bands;
        int n = bands.Length;
        if (n == 0)
        {
            return;
        }

        if (_smoothed.Length != n)
        {
            _smoothed = new float[n];
        }

        for (int i = 0; i < n; i++)
        {
            float target = Math.Clamp(bands[i], 0f, 1f);
            float smoothing = target > _smoothed[i] ? AttackSmoothing : ReleaseSmoothing;
            _smoothed[i] += (target - _smoothed[i]) * smoothing;
        }

        int bassCount = Math.Max(1, n / 4);
        float bassAvg = Average(_smoothed, 0, bassCount);

        _rotationDegrees += (BaseRotationSpeedDegPerSec + bassAvg * BassRotationBoostDegPerSec) * dt;
        _rotationDegrees %= 360f;

        float minSide = Math.Min(info.Width, info.Height);
        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float innerRadius = minSide * 0.12f;
        float maxBarLength = minSide * 0.38f;
        float barWidth = Math.Clamp(minSide * 0.006f, 1.5f, 6f);
        float angleStep = 360f / n;

        canvas.Save();
        canvas.Translate(centerX, centerY);
        canvas.RotateDegrees(_rotationDegrees);

        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = barWidth * 2.6f,
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(barWidth, barWidth),
        };
        using var barPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = barWidth,
        };

        for (int i = 0; i < n; i++)
        {
            float level = _smoothed[i];
            float angleDeg = i * angleStep;
            float rad = angleDeg * MathF.PI / 180f;

            var color = SKColor.FromHsv(angleDeg % 360f, 85f, Math.Clamp(40f + level * 60f, 40f, 100f));
            byte alpha = (byte)Math.Clamp(120f + level * 135f, 120f, 255f);

            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);
            float x0 = cos * innerRadius;
            float y0 = sin * innerRadius;
            float len = innerRadius + level * maxBarLength;
            float x1 = cos * len;
            float y1 = sin * len;

            glowPaint.Color = color.WithAlpha((byte)(alpha * 0.55f));
            canvas.DrawLine(x0, y0, x1, y1, glowPaint);

            barPaint.Color = color.WithAlpha(alpha);
            canvas.DrawLine(x0, y0, x1, y1, barPaint);
        }

        canvas.Restore();

        using var centerPaint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(minSide * 0.03f, minSide * 0.03f),
        };
        float centerPulseRadius = innerRadius * (0.5f + bassAvg * 0.6f);
        centerPaint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(80f + bassAvg * 150f, 80f, 220f));
        canvas.DrawCircle(centerX, centerY, centerPulseRadius, centerPaint);
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
