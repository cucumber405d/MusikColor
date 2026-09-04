namespace MusikColor.Contracts;

/// <summary>
/// Порт "приёмника" нормализованного потока — то, во что ядро публикует
/// готовые частотные кадры. Реализуется адаптером визуализации.
/// </summary>
public interface IVisualizationSink
{
    void Publish(FrequencyFrame frame);
}
