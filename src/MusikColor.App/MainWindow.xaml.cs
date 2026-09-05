using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MusikColor.Adapters.Capture;
using MusikColor.Adapters.Visualization;
using MusikColor.Contracts;
using MusikColor.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace MusikColor.App;

public partial class MainWindow : Window
{
    private const int BandCount = 48;

    private readonly VisualizationHost _visualizationHost = new();
    private readonly List<IVisualizerPlugin> _plugins;

    private IAudioSource? _audioSource;
    private SpectrumEngine? _engine;
    private bool _running;

    public MainWindow()
    {
        InitializeComponent();

        var pluginsPath = Path.Combine(AppContext.BaseDirectory, "Plugins");
        _plugins = new List<IVisualizerPlugin>(new PluginLoader(pluginsPath).LoadAll());

        var bandFrequencies = VisualizerContext.ComputeLogSpacedCenterFrequencies(BandCount);
        var visualizerContext = new VisualizerContext(BandCount, bandFrequencies);
        foreach (var plugin in _plugins)
        {
            plugin.Init(visualizerContext);
        }

        PluginCombo.ItemsSource = _plugins;
        if (_plugins.Count > 0)
        {
            PluginCombo.SelectedIndex = 0;
            _visualizationHost.ActivePlugin = _plugins[0];
        }

        SourceCombo.SelectedIndex = 0;
        RefreshDeviceList();

        CompositionTarget.Rendering += (_, _) => Canvas.InvalidateVisual();
    }

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDeviceList();

    private void RefreshDeviceList()
    {
        if (DeviceCombo == null)
        {
            return;
        }

        var tag = (SourceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        bool isMicrophone = tag == "microphone";

        var devices = isMicrophone ? AudioDeviceCatalog.CaptureDevices() : AudioDeviceCatalog.RenderDevices();
        DeviceCombo.ItemsSource = devices;

        if (devices.Count == 0)
        {
            return;
        }

        // Предвыбираем устройство "по умолчанию" — то, через которое Windows
        // реально сейчас играет звук (или слушает микрофон), а не первое
        // в списке: иначе легко попасть на неиспользуемый цифровой выход.
        string? defaultId = isMicrophone
            ? AudioDeviceCatalog.DefaultCaptureDeviceId()
            : AudioDeviceCatalog.DefaultRenderDeviceId();

        int index = defaultId != null
            ? devices.ToList().FindIndex(d => d.Id == defaultId)
            : -1;

        DeviceCombo.SelectedIndex = index >= 0 ? index : 0;
    }

    private void PluginCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PluginCombo.SelectedItem is IVisualizerPlugin plugin)
        {
            _visualizationHost.ActivePlugin = plugin;
        }
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            StopCapture();
        }
        else
        {
            StartCapture();
        }
    }

    private void StartCapture()
    {
        var tag = (SourceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var deviceInfo = DeviceCombo.SelectedItem as AudioDeviceInfo;
        var device = deviceInfo != null ? AudioDeviceCatalog.ById(deviceInfo.Id) : null;

        IAudioSource source = tag == "microphone"
            ? new MicrophoneAudioSource(device)
            : new LoopbackAudioSource(device);

        source.ErrorOccurred += OnAudioSourceError;

        _audioSource = source;
        _engine = new SpectrumEngine(source, _visualizationHost, BandCount);
        _engine.Start();

        _running = true;
        StartStopButton.Content = "Стоп";
        SourceCombo.IsEnabled = false;
        DeviceCombo.IsEnabled = false;
    }

    private void OnAudioSourceError(object? sender, Exception ex)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(this, ex.ToString(), "Ошибка захвата звука", MessageBoxButton.OK, MessageBoxImage.Error);
            StopCapture();
        });
    }

    private void StopCapture()
    {
        if (_audioSource != null)
        {
            _audioSource.ErrorOccurred -= OnAudioSourceError;
        }

        _engine?.Dispose();
        _engine = null;
        _audioSource = null;

        _running = false;
        StartStopButton.Content = "Старт";
        SourceCombo.IsEnabled = true;
        DeviceCombo.IsEnabled = true;
    }

    private void Canvas_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        var frame = _visualizationHost.TryGetLatest();
        var plugin = _visualizationHost.ActivePlugin;

        if (plugin == null || frame == null)
        {
            canvas.Clear(new SKColor(10, 10, 18));
            return;
        }

        plugin.Render(canvas, info, frame);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopCapture();
        base.OnClosed(e);
    }
}
