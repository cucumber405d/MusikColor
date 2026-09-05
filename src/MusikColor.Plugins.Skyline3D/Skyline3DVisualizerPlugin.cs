using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.Skyline3D;

/// <summary>
/// "3D-скайлайн-бары" — тот же частотный спектр, но каждая полоса
/// нарисована как псевдо-3D "здание" (параллелепипед с передней, верхней
/// и боковой гранью, как в старых 3D-бар-чартах), с окнами-точками и
/// мягким свечением по громкости. Слева направо (бас -> верха) цвет
/// зданий плавно уходит от сине-фиолетового к тёплому оранжево-золотому,
/// как ночной город на закате — зелёный намеренно не используется.
/// </summary>
public sealed class Skyline3DVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "skyline-3d";
    public string DisplayName => "3D-скайлайн";

    private const float AttackSmoothing = 0.5f;
    private const float ReleaseSmoothing = 0.06f;

    private static readonly SKColor LeftColor = new(90, 60, 200);
    private static readonly SKColor RightColor = new(255, 150, 40);

    private float[] _smoothed = Array.Empty<float>();

    public void Init(VisualizerContext context)
    {
        _smoothed = new float[Math.Max(1, context.BandCount)];
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        DrawSky(canvas, info);

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

            float t = n > 1 ? (float)i / (n - 1) : 0f;
            var baseColor = LerpColor(LeftColor, RightColor, t);

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

    private static SKColor LerpColor(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte r = (byte)(a.Red + (b.Red - a.Red) * t);
        byte g = (byte)(a.Green + (b.Green - a.Green) * t);
        byte bl = (byte)(a.Blue + (b.Blue - a.Blue) * t);
        return new SKColor(r, g, bl);
    }

    private static SKColor Shade(SKColor color, float factor)
    {
        byte r = (byte)Math.Clamp(color.Red * factor, 0f, 255f);
        byte g = (byte)Math.Clamp(color.Green * factor, 0f, 255f);
        byte b = (byte)Math.Clamp(color.Blue * factor, 0f, 255f);
        return new SKColor(r, g, b);
    }
}
