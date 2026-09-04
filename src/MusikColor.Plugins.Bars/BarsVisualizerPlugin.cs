using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.Bars;

/// <summary>
/// Классические частотные бары: каждая полоса — вертикальный столбик,
/// цвет которого зависит от частоты (радуга слева направо — как в старых
/// эквалайзерах и "цветомузыке"). Громкость и бит добавляют лёгкую вспышку.
/// </summary>
public sealed class BarsVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "bars";
    public string DisplayName => "Частотные бары";

    public void Init(VisualizerContext context)
    {
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(8, 8, 16));

        int bandCount = frame.Bands.Length;
        if (bandCount == 0 || info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        const float gap = 2f;
        float slotWidth = (float)info.Width / bandCount;
        float barWidth = Math.Max(1f, slotWidth - gap);

        using var paint = new SKPaint { IsAntialias = true };

        for (int i = 0; i < bandCount; i++)
        {
            float value = frame.Bands[i];
            float barHeight = value * info.Height;

            float hue = 260f - 260f * i / bandCount; // от фиолетового (бас) к красному (верха)
            paint.Color = SKColor.FromHsv(hue, 90, 60 + 40 * value);

            float x = i * slotWidth + gap / 2f;
            var rect = new SKRect(x, info.Height - barHeight, x + barWidth, info.Height);
            canvas.DrawRect(rect, paint);
        }

        if (frame.Beat)
        {
            using var flashPaint = new SKPaint { Color = new SKColor(255, 255, 255, 40) };
            canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), flashPaint);
        }
    }
}
