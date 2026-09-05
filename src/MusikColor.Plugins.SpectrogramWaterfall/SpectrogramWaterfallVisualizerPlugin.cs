using System;
using System.Runtime.InteropServices;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.SpectrogramWaterfall;

/// <summary>
/// "Спектрограмма-водопад" — как в профессиональных аудиоанализаторах
/// и SDR-приёмниках: по горизонтали — частота (бас слева, верха справа),
/// по вертикали — время. Новая строка появляется сверху и постепенно
/// уезжает вниз, пока не исчезнет с экрана.
///
/// Цвет — монохромная ледяная термокарта интенсивности (почти чёрный ->
/// тёмно-синий -> электрик -> голубовато-белый -> белый), совсем не
/// похожая на разноцветные палитры остальных плагинов: здесь цвет
/// говорит только о громкости конкретной частоты в конкретный момент.
/// </summary>
public sealed class SpectrogramWaterfallVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "spectrogram-waterfall";
    public string DisplayName => "Спектрограмма-водопад";

    private const int HistoryRows = 240;
    private const double RowIntervalMs = 22.0; // ~45 строк/сек, не зависит от Hz монитора

    private static readonly (float Pos, SKColor Color)[] Stops =
    {
        (0.00f, new SKColor(2, 2, 10)),
        (0.28f, new SKColor(10, 20, 90)),
        (0.55f, new SKColor(20, 90, 200)),
        (0.80f, new SKColor(120, 200, 255)),
        (1.00f, new SKColor(255, 255, 255)),
    };

    private SKBitmap? _bitmap;
    private byte[] _pixels = Array.Empty<byte>();
    private int _bandCount;
    private int _rowBytes;
    private DateTime _lastRowTime = DateTime.MinValue;

    public void Init(VisualizerContext context)
    {
        AllocateBitmap(Math.Max(1, context.BandCount));
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(2, 2, 10));

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

        if (_bitmap == null || _bandCount != n)
        {
            AllocateBitmap(n);
        }

        var now = DateTime.UtcNow;
        if ((now - _lastRowTime).TotalMilliseconds >= RowIntervalMs)
        {
            _lastRowTime = now;
            PushNewRow(bands);
        }

        Marshal.Copy(_pixels, 0, _bitmap!.GetPixels(), _pixels.Length);

        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(_bitmap, new SKRect(0, 0, info.Width, info.Height), paint);
    }

    private void AllocateBitmap(int bandCount)
    {
        _bitmap?.Dispose();
        _bitmap = new SKBitmap(new SKImageInfo(bandCount, HistoryRows, SKColorType.Rgba8888, SKAlphaType.Opaque));
        _rowBytes = _bitmap.RowBytes;
        _pixels = new byte[_rowBytes * HistoryRows];
        _bandCount = bandCount;
        _lastRowTime = DateTime.MinValue;
    }

    /// <summary>
    /// Сдвигает всю историю на одну строку вниз (memmove по массиву) и
    /// записывает новую строку наверх — так новые данные всегда
    /// появляются сверху и "стекают" вниз, как в настоящем водопаде.
    /// </summary>
    private void PushNewRow(float[] bands)
    {
        Buffer.BlockCopy(_pixels, 0, _pixels, _rowBytes, (HistoryRows - 1) * _rowBytes);

        for (int i = 0; i < _bandCount && i < bands.Length; i++)
        {
            var color = ColorMapFor(bands[i]);
            int offset = i * 4;
            _pixels[offset + 0] = color.Red;
            _pixels[offset + 1] = color.Green;
            _pixels[offset + 2] = color.Blue;
            _pixels[offset + 3] = 255;
        }
    }

    private static SKColor ColorMapFor(float value)
    {
        float v = Math.Clamp(value, 0f, 1f);

        for (int i = 0; i < Stops.Length - 1; i++)
        {
            if (v <= Stops[i + 1].Pos)
            {
                float span = Stops[i + 1].Pos - Stops[i].Pos;
                float localT = span > 0f ? (v - Stops[i].Pos) / span : 0f;
                return LerpColor(Stops[i].Color, Stops[i + 1].Color, localT);
            }
        }

        return Stops[^1].Color;
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
