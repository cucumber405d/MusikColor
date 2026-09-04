using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.Bars;

/// <summary>
/// Классический "блочный" эквалайзер — как в железных LED-панелях и
/// старых Winamp-визуализациях: каждая полоса рисуется не сплошным
/// прямоугольником, а стопкой отдельных светящихся сегментов с зазорами,
/// плюс мягкое свечение под ними. Цвет столбца зависит от частоты
/// (радуга: синий бас -> красные верха). Сверху — "пиковый" сегмент,
/// который держится и медленно опадает, как индикатор пика на настоящих
/// LED-эквалайзерах.
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

    private float[] _peak = Array.Empty<float>();
    private float[] _peakHoldTimer = Array.Empty<float>();

    public void Init(VisualizerContext context)
    {
        _peak = new float[context.BandCount];
        _peakHoldTimer = new float[context.BandCount];
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(6, 6, 12));

        int bandCount = frame.Bands.Length;
        if (bandCount == 0 || info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        if (_peak.Length != bandCount)
        {
            _peak = new float[bandCount];
            _peakHoldTimer = new float[bandCount];
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

            float hue = 240f - 240f * i / bandCount; // синий (бас) -> красный (верха)
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
