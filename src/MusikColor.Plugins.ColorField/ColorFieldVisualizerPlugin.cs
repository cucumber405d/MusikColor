using System;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.ColorField;

/// <summary>
/// "Цветомузыка" для тёмного зала с большим экраном: не аккуратная сетка
/// лампочек, а постоянный поток цвета — несколько крупных мягких пятен
/// света, дрейфующих по экрану и смешивающихся аддитивно, как настоящие
/// сценические прожекторы. Поверх — рассыпанные по экрану точки; когда
/// приходит резкий удар "своей" частоты, точка вспыхивает сверхновой —
/// большой яркой вспышкой с лучами, после чего медленно гаснет обратно
/// до маленькой точки.
///
/// Три канала на реальных частотах: бас -> красный, середина -> жёлтый,
/// верха -> зелёный. Тишина -> синий — базовый цвет, из которого
/// цветные каналы "отъедают" долю по мере появления сигнала (общий
/// "бюджет" яркости постоянен, меняется только его цветовой состав).
/// </summary>
public sealed class ColorFieldVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "color-field";
    public string DisplayName => "Цветомузыка";

    private const int LampCount = 160;
    private const int BurstDurationFrames = 26;
    private const int MaxBurstsPerOnset = 5;

    private static readonly SKColor BassColor = new(230, 40, 40);
    private static readonly SKColor MidColor = new(240, 200, 30);
    private static readonly SKColor TrebleColor = new(60, 220, 100);
    private static readonly SKColor SilenceColor = new(30, 70, 230);

    private readonly Random _random = new();

    private Lamp[] _lamps = Array.Empty<Lamp>();
    private int[] _burstTimer = Array.Empty<int>();
    private Blob[] _blobs = Array.Empty<Blob>();
    private int _frame;

    private float _bassShare, _midShare, _trebleShare;
    private float _bassFloor, _midFloor, _trebleFloor;

    private struct Lamp
    {
        public float X;
        public float Y;
        public float Radius;
        public int ReshuffleAt;
    }

    private struct Blob
    {
        public int Band; // 0 бас, 1 середина, 2 верха, 3 тишина
        public float PhaseX;
        public float PhaseY;
        public float SpeedX;
        public float SpeedY;
    }

    public void Init(VisualizerContext context)
    {
        _lamps = new Lamp[LampCount];
        _burstTimer = new int[LampCount];
        for (int i = 0; i < _lamps.Length; i++)
        {
            _lamps[i] = NewLamp(i);
        }

        // Несколько крупных пятен света на канал — дают постоянный
        // дрейфующий фон вместо статичной заливки.
        _blobs = new Blob[8];
        for (int i = 0; i < _blobs.Length; i++)
        {
            _blobs[i] = new Blob
            {
                Band = i % 4,
                PhaseX = (float)(_random.NextDouble() * Math.PI * 2),
                PhaseY = (float)(_random.NextDouble() * Math.PI * 2),
                SpeedX = 0.15f + (float)_random.NextDouble() * 0.2f,
                SpeedY = 0.12f + (float)_random.NextDouble() * 0.2f,
            };
        }
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        canvas.Clear(new SKColor(4, 4, 10));

        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        _frame++;

        var (bass, mid, treble) = SplitBands(frame.Bands);

        float total = bass + mid + treble;
        float targetBass, targetMid, targetTreble;
        if (total < 0.02f)
        {
            targetBass = targetMid = targetTreble = 0f;
        }
        else
        {
            float scale = Math.Min(1f, total) / total;
            targetBass = bass * scale;
            targetMid = mid * scale;
            targetTreble = treble * scale;
        }

        const float smoothing = 0.2f;
        _bassShare += (targetBass - _bassShare) * smoothing;
        _midShare += (targetMid - _midShare) * smoothing;
        _trebleShare += (targetTreble - _trebleShare) * smoothing;

        DetectOnsetAndBurst(ref _bassFloor, bass, 0);
        DetectOnsetAndBurst(ref _midFloor, mid, 1);
        DetectOnsetAndBurst(ref _trebleFloor, treble, 2);

        RenderGlowWash(canvas, info);
        RenderLamps(canvas, info);
    }

    private void RenderGlowWash(SKCanvas canvas, SKImageInfo info)
    {
        float minSide = Math.Min(info.Width, info.Height);

        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(minSide * 0.12f, minSide * 0.12f),
        };

        foreach (var blob in _blobs)
        {
            float level = blob.Band switch
            {
                0 => _bassShare,
                1 => _midShare,
                2 => _trebleShare,
                _ => Math.Max(0.15f, 1f - (_bassShare + _midShare + _trebleShare)),
            };

            if (level < 0.02f)
            {
                continue;
            }

            var color = blob.Band switch
            {
                0 => BassColor,
                1 => MidColor,
                2 => TrebleColor,
                _ => SilenceColor,
            };

            float t = _frame * 0.01f;
            float x = (0.5f + 0.38f * MathF.Sin(t * blob.SpeedX + blob.PhaseX)) * info.Width;
            float y = (0.5f + 0.38f * MathF.Cos(t * blob.SpeedY + blob.PhaseY)) * info.Height;
            float radius = minSide * (0.16f + 0.34f * level);

            glowPaint.Color = color.WithAlpha((byte)Math.Clamp(70f + level * 140f, 0f, 255f));
            canvas.DrawCircle(x, y, radius, glowPaint);
        }
    }

    private void RenderLamps(SKCanvas canvas, SKImageInfo info)
    {
        int bassLamps = (int)(LampCount * _bassShare);
        int midLamps = (int)(LampCount * _midShare);
        int trebleLamps = (int)(LampCount * _trebleShare);

        using var paint = new SKPaint { IsAntialias = true };
        using var burstGlowPaint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(18f, 18f),
        };
        using var corePaint = new SKPaint { IsAntialias = true };
        using var rayPaint = new SKPaint
        {
            IsAntialias = true,
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
        };

        for (int i = 0; i < _lamps.Length; i++)
        {
            ref var lamp = ref _lamps[i];
            if (_frame >= lamp.ReshuffleAt)
            {
                lamp = NewLamp(i);
            }

            SKColor color = i < bassLamps ? BassColor
                : i < bassLamps + midLamps ? MidColor
                : i < bassLamps + midLamps + trebleLamps ? TrebleColor
                : SilenceColor;

            float px = lamp.X * info.Width;
            float py = lamp.Y * info.Height;

            int burst = _burstTimer[i];
            if (burst > 0)
            {
                float k = burst / (float)BurstDurationFrames;
                float burstRadius = lamp.Radius + 70f * k;

                burstGlowPaint.Color = color.WithAlpha((byte)(200 * k));
                canvas.DrawCircle(px, py, burstRadius, burstGlowPaint);

                corePaint.Color = new SKColor(255, 255, 255, (byte)(230 * k));
                canvas.DrawCircle(px, py, lamp.Radius + 6f * k, corePaint);

                rayPaint.Color = color.WithAlpha((byte)(180 * k));
                float rayLen = burstRadius * 1.6f;
                for (int r = 0; r < 4; r++)
                {
                    float angle = r * MathF.PI / 4f + _frame * 0.02f;
                    float dx = MathF.Cos(angle) * rayLen;
                    float dy = MathF.Sin(angle) * rayLen;
                    canvas.DrawLine(px - dx, py - dy, px + dx, py + dy, rayPaint);
                }

                _burstTimer[i] = burst - 1;
            }
            else
            {
                paint.Color = color.WithAlpha(210);
                canvas.DrawCircle(px, py, lamp.Radius, paint);
            }
        }
    }

    private void DetectOnsetAndBurst(ref float floor, float value, int band)
    {
        bool isOnset = value > floor * 1.6f + 0.03f && value > 0.1f;
        floor += (value - floor) * 0.03f;

        if (!isOnset)
        {
            return;
        }

        int bassLamps = (int)(LampCount * _bassShare);
        int midLamps = (int)(LampCount * _midShare);
        int trebleLamps = (int)(LampCount * _trebleShare);

        int rangeStart, rangeEnd;
        switch (band)
        {
            case 0:
                rangeStart = 0;
                rangeEnd = Math.Max(rangeStart, bassLamps);
                break;
            case 1:
                rangeStart = bassLamps;
                rangeEnd = Math.Max(rangeStart, bassLamps + midLamps);
                break;
            default:
                rangeStart = bassLamps + midLamps;
                rangeEnd = Math.Max(rangeStart, bassLamps + midLamps + trebleLamps);
                break;
        }

        int rangeSize = rangeEnd - rangeStart;
        if (rangeSize <= 0)
        {
            return;
        }

        int bursts = Math.Min(MaxBurstsPerOnset, rangeSize);
        for (int b = 0; b < bursts; b++)
        {
            int idx = rangeStart + _random.Next(rangeSize);
            _burstTimer[idx] = BurstDurationFrames;
        }
    }

    private Lamp NewLamp(int seedOffset)
    {
        return new Lamp
        {
            X = (float)_random.NextDouble(),
            Y = (float)_random.NextDouble(),
            Radius = 2.5f + (float)_random.NextDouble() * 4f,
            ReshuffleAt = _frame + 90 + _random.Next(0, 180) + seedOffset % 30,
        };
    }

    private static (float bass, float mid, float treble) SplitBands(float[] bands)
    {
        int n = bands.Length;
        if (n == 0)
        {
            return (0f, 0f, 0f);
        }

        int thirdA = Math.Max(1, n / 3);
        int thirdB = Math.Max(thirdA + 1, n * 2 / 3);

        float bass = Average(bands, 0, thirdA);
        float mid = Average(bands, thirdA, thirdB);
        float treble = Average(bands, thirdB, n);
        return (bass, mid, treble);
    }

    private static float Average(float[] values, int start, int end)
    {
        float sum = 0f;
        int count = 0;
        for (int i = start; i < end && i < values.Length; i++)
        {
            sum += values[i];
            count++;
        }
        return count > 0 ? sum / count : 0f;
    }
}
