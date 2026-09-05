using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.MirrorSpectrum;

/// <summary>
/// "Зеркальный спектр" — классический вид анализатора спектра: полосы
/// растут вверх от горизонтальной линии слева направо (бас -> верха),
/// а под этой линией — их приглушённое, размытое отражение, как в
/// стекле или на глянцевом танцполе. Цвет каждой полосы — от
/// сине-фиолетового (тихо) до красно-оранжевого (громко); зелёный
/// намеренно не используется (по итогам предыдущих правок — зелёный
/// свет на лицах танцующих не годится).
/// </summary>
public sealed class MirrorSpectrumVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "mirror-spectrum";
    public string DisplayName => "Зеркальный спектр";

    private const float AttackSmoothing = 0.55f;
    private const float ReleaseSmoothing = 0.09f;

    private static readonly SKColor QuietColor = new(90, 40, 200);
    private static readonly SKColor LoudColor = new(255, 60, 40);

    private float[] _smoothed = Array.Empty<float>();

    public void Init(VisualizerContext context)
    {
        _smoothed = new float[Math.Max(1, context.BandCount)];
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

                var color = LerpColor(QuietColor, LoudColor, level);
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

                var color = LerpColor(QuietColor, LoudColor, level);
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

    private static SKColor LerpColor(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte r = (byte)(a.Red + (b.Red - a.Red) * t);
        byte g = (byte)(a.Green + (b.Green - a.Green) * t);
        byte bl = (byte)(a.Blue + (b.Blue - a.Blue) * t);
        return new SKColor(r, g, bl);
    }
}
