using MusikColor.Contracts;

namespace MusikColor.Adapters.Visualization;

/// <summary>
/// Адаптер между ядром и слоем отрисовки. Принимает нормализованный поток
/// частотных кадров от Core (IVisualizationSink) и хранит последний кадр —
/// UI рисует на своей собственной частоте кадров (обычно 60 fps),
/// независимо от того, как часто приходят аудио-коллбэки.
/// </summary>
public sealed class VisualizationHost : IVisualizationSink
{
    private readonly object _gate = new();
    private FrequencyFrame? _latest;

    public IVisualizerPlugin? ActivePlugin { get; set; }

    public void Publish(FrequencyFrame frame)
    {
        lock (_gate)
        {
            _latest = frame;
        }
    }

    public FrequencyFrame? TryGetLatest()
    {
        lock (_gate)
        {
            return _latest;
        }
    }
}
