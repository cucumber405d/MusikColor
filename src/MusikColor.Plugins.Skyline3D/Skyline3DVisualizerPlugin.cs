using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.Skyline3D;

/// <summary>
/// "3D-скайлайн-бары" — тот же частотный спектр, но каждая полоса
/// нарисована как псевдо-3D "здание" (параллелепипед с передней, верхней
/// и боковой гранью, как в старых 3D-бар-чартах), с окнами-точками и
/// мягким свечением по громкости.
///
/// Цвет — 6-ступенчатая палитра по дуге оттенков (не гладкий градиент
/// слева направо, а перемежающийся: соседние здания берут соседние по
/// палитре оттенки вперемешку, детерминированный псевдослучайный сдвиг
/// по индексу — как в настоящем городе, где тёплые и холодные окна
/// соседствуют). Сама дуга не застыла навечно — раз в несколько секунд
/// весь город плавно перекрашивается в новую гамму (например, с
/// закатной на ледяную), чтобы при долгом просмотре не приедался один
/// и тот же фиолетово-золотой закат.
/// </summary>
public sealed class Skyline3DVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "skyline-3d";
    public string DisplayName => "3D-скайлайн";

    private const float AttackSmoothing = 0.5f;
    private const float ReleaseSmoothing = 0.06f;

    // Относительные смещения оттенка внутри палитры (градусы) — сама
    // дуга едет по кругу вместе с базовым оттенком, но взаимный рисунок
    // "холоднее -> теплее" всегда сохраняется.
    private static readonly float[] PaletteHueOffsets = { 0f, 35f, 80f, 130f, 170f, 205f };
    private static readonly float[] PaletteSaturation = { 65f, 70f, 75f, 80f, 80f, 75f };
    private static readonly float[] PaletteValue = { 75f, 72f, 80f, 92f, 100f, 100f };

    private static readonly TimeSpan ChangeInterval = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(2000);

    private readonly Random _random = new();
    private readonly SKColor[] _palette = new SKColor[PaletteHueOffsets.Length];

    private float[] _smoothed = Array.Empty<float>();
    private HueState _hue;

    private struct HueState
    {
        public float FromHue;
        public float ToHue;
        public DateTime TransitionStart;
        public DateTime NextChangeAt;
    }

    public void Init(VisualizerContext context)
    {
        _smoothed = new float[Math.Max(1, context.BandCount)];

        var now = DateTime.UtcNow;
        float initialHue = RandomHue();
        _hue = new HueState
        {
            FromHue = initialHue,
            ToHue = initialHue,
            TransitionStart = now,
            NextChangeAt = now + ChangeInterval,
        };
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        DrawSky(canvas, info);

        var now = DateTime.UtcNow;
        float baseHue = AdvanceAndGetHue(now);
        for (int p = 0; p < _palette.Length; p++)
        {
            float hue = (baseHue + PaletteHueOffsets[p]) % 360f;
            _palette[p] = SKColor.FromHsv(hue, PaletteSaturation[p], PaletteValue[p]);
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

        float baseline = info.Height * 0.92f;
        float maxHeight = info.Height * 0.72f;

        float slot = (float)info.Width / n;
        float buildingWidth = Math.Max(2f, slot * 0.7f);
        float gap = (slot - buildingWidth) / 2f;
        float depthX = buildingWidth * 0.32f;
        float depthY = depthX * 0.55f;

        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(10f, 10f),
        };
        using var facePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var windowPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        for (int i = 0; i < n; i++)
        {
            float level = _smoothed[i];
            if (level <= 0.004f)
            {
                continue;
            }

            float height = Math.Max(2f, level * maxHeight);
            float x0 = i * slot + gap;
            float x1 = x0 + buildingWidth;
            float yTop = baseline - height;
            float yBottom = baseline;

            var baseColor = PaletteColorFor(i, n, _palette);

            var frontColor = Shade(baseColor, 0.55f);
            var sideColor = Shade(baseColor, 0.78f);
            var topColor = Shade(baseColor, 1.25f);

            // Мягкое свечение позади здания — тем ярче, чем громче полоса.
            glowPaint.Color = baseColor.WithAlpha((byte)Math.Clamp(60f + level * 140f, 60f, 200f));
            canvas.DrawRect(new SKRect(x0 - depthX * 0.5f, yTop - depthY, x1 + depthX, yBottom), glowPaint);

            // Верхняя грань (параллелограмм) — здание "смотрит" вверх-вправо.
            using (var topPath = new SKPath())
            {
                topPath.MoveTo(x0, yTop);
                topPath.LineTo(x1, yTop);
                topPath.LineTo(x1 + depthX, yTop - depthY);
                topPath.LineTo(x0 + depthX, yTop - depthY);
                topPath.Close();
                facePaint.Color = topColor;
                canvas.DrawPath(topPath, facePaint);
            }

            // Правая боковая грань.
            using (var sidePath = new SKPath())
            {
                sidePath.MoveTo(x1, yTop);
                sidePath.LineTo(x1 + depthX, yTop - depthY);
                sidePath.LineTo(x1 + depthX, yBottom - depthY);
                sidePath.LineTo(x1, yBottom);
                sidePath.Close();
                facePaint.Color = sideColor;
                canvas.DrawPath(sidePath, facePaint);
            }

            // Передняя грань.
            facePaint.Color = frontColor;
            canvas.DrawRect(new SKRect(x0, yTop, x1, yBottom), facePaint);

            DrawWindows(canvas, windowPaint, i, x0, buildingWidth, yTop, yBottom);
        }
    }

    /// <summary>
    /// Детерминированная (не мигающая случайно каждый кадр) сетка окон:
    /// какое окно "горит" зависит только от индекса здания, ряда и
    /// колонки, поэтому картина стабильна между кадрами и оживает только
    /// когда меняется высота самого здания.
    /// </summary>
    private static void DrawWindows(SKCanvas canvas, SKPaint paint, int bandIndex, float x0, float width, float yTop, float yBottom)
    {
        const float rowPitch = 13f;
        const float colPitch = 9f;
        const float windowSize = 4f;

        int columns = Math.Max(1, (int)((width - 4f) / colPitch));

        for (float y = yBottom - rowPitch; y > yTop + windowSize; y -= rowPitch)
        {
            int row = (int)((yBottom - y) / rowPitch);
            for (int col = 0; col < columns; col++)
            {
                int hash = (bandIndex * 911 + row * 173 + col * 57) % 5;
                if (hash != 0)
                {
                    continue;
                }

                float wx = x0 + 3f + col * colPitch;
                bool warm = ((bandIndex + row + col) % 3) == 0;
                paint.Color = warm ? new SKColor(255, 214, 140, 190) : new SKColor(200, 220, 255, 150);
                canvas.DrawRect(new SKRect(wx, y - windowSize, wx + windowSize, y), paint);
            }
        }
    }

    private static void DrawSky(SKCanvas canvas, SKImageInfo info)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, info.Height),
            new[] { new SKColor(6, 4, 20), new SKColor(30, 14, 40), new SKColor(60, 24, 40) },
            new[] { 0f, 0.75f, 1f },
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), paint);
    }

    /// <summary>
    /// Позиция здания задаёт "макро"-положение в закатной палитре
    /// (слева теплее к фиолетовому, справа — к золотому), а
    /// детерминированный псевдослучайный сдвиг по индексу перемешивает
    /// соседние здания между собой, чтобы цвет не сливался в гладкую
    /// радугу, а перемежался, как настоящая городская застройка.
    /// </summary>
    private static SKColor PaletteColorFor(int index, int count, SKColor[] palette)
    {
        float t = count > 1 ? (float)index / (count - 1) : 0f;
        float posIndex = t * (palette.Length - 1);
        int baseIdx = (int)MathF.Round(posIndex);

        int jitter = (Hash(index) % 3) - 1; // -1, 0 или +1
        int idx = Math.Clamp(baseIdx + jitter, 0, palette.Length - 1);
        return palette[idx];
    }

    private static int Hash(int i)
    {
        unchecked
        {
            uint h = (uint)i * 2654435761u;
            return (int)(h & 0x7fffffff);
        }
    }

    private float AdvanceAndGetHue(DateTime now)
    {
        if (now >= _hue.NextChangeAt)
        {
            _hue.FromHue = InterpolateHue(_hue, now);
            _hue.ToHue = RandomHue();
            _hue.TransitionStart = now;
            _hue.NextChangeAt = now + ChangeInterval;
        }

        return InterpolateHue(_hue, now);
    }

    private static float InterpolateHue(in HueState state, DateTime now)
    {
        double elapsed = (now - state.TransitionStart).TotalMilliseconds;
        float t = (float)Math.Clamp(elapsed / TransitionDuration.TotalMilliseconds, 0.0, 1.0);
        return LerpHue(state.FromHue, state.ToHue, t);
    }

    private static float LerpHue(float from, float to, float t)
    {
        float diff = to - from;
        diff = ((diff + 540f) % 360f) - 180f;
        float result = from + diff * t;
        return ((result % 360f) + 360f) % 360f;
    }

    private float RandomHue() => (float)(_random.NextDouble() * 360.0);

    private static SKColor Shade(SKColor color, float factor)
    {
        byte r = (byte)Math.Clamp(color.Red * factor, 0f, 255f);
        byte g = (byte)Math.Clamp(color.Green * factor, 0f, 255f);
        byte b = (byte)Math.Clamp(color.Blue * factor, 0f, 255f);
        return new SKColor(r, g, b);
    }
}
