using SkiaSharp;

namespace MusikColor.Contracts;

/// <summary>
/// Контракт плагина визуализации. На входе — нормализованные частоты,
/// на выходе — рисунок на SkiaSharp-холсте. Реализация ничего не знает
/// ни про WASAPI, ни про WPF — только про SkiaSharp и FrequencyFrame.
/// </summary>
public interface IVisualizerPlugin
{
    /// <summary>Уникальный технический идентификатор плагина.</summary>
    string Id { get; }

    /// <summary>Имя, которое видит пользователь в списке визуализаций.</summary>
    string DisplayName { get; }

    void Init(VisualizerContext context);

    void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame);
}
