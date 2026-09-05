using SkiaSharp;
using MusikColor.Contracts;
using MusikColor.Plugins.Shared;

namespace MusikColor.Plugins.ColorField;

/// <summary>
/// "Цветомузыка" — тонкая обёртка над общим StarFieldRenderer
/// (см. MusikColor.Plugins.Shared). Вся логика вынесена туда, чтобы её
/// же переиспользовать как фон в плагине "Генеративная нотация".
/// </summary>
public sealed class ColorFieldVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "color-field";
    public string DisplayName => "Цветомузыка";

    private readonly StarFieldRenderer _renderer = new();

    public void Init(VisualizerContext context) => _renderer.Init();

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame) => _renderer.Render(canvas, info, frame);
}
