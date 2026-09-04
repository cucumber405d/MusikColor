using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.ColorField;

/// <summary>
/// "Цветомузыка": весь экран заливается цветом, который плывёт в
/// зависимости от того, какая частотная область сейчас доминирует —
/// бас тянет к красному, середина к зелёному, верха к синему/фиолетовому.
/// Громкость управляет яркостью, резкий бит даёт вспышку-кольцо.
/// </summary>
public sealed class ColorFieldVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "color-field";
    public string DisplayName => "Цветомузыка";

    private float _hue;

    public void Init(VisualizerContext context)
    {
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        if (frame.Bands.Length == 0)
        {
            canvas.Clear(SKColors.Black);
            return;
        }

        // "Центр тяжести" спектра — куда сейчас смещена энергия сигнала.
        float weightedSum = 0f;
        float totalWeight = 0f;
        for (int i = 0; i < frame.Bands.Length; i++)
        {
            weightedSum += i * frame.Bands[i];
            totalWeight += frame.Bands[i];
        }
        float centroid = totalWeight > 0.001f ? weightedSum / totalWeight / frame.Bands.Length : 0f;

        float targetHue = 360f * centroid;
        _hue += (targetHue - _hue) * 0.15f;

        float brightness = 20f + 70f * Math.Min(1f, frame.Volume * 1.4f);
        var color = SKColor.FromHsv(_hue, 75, brightness);

        canvas.Clear(color);

        if (frame.Beat)
        {
            using var pulsePaint = new SKPaint { Color = new SKColor(255, 255, 255, 60) };
            float radius = Math.Min(info.Width, info.Height) * 0.4f;
            canvas.DrawCircle(info.Width / 2f, info.Height / 2f, radius, pulsePaint);
        }
    }
}
