using System;
using System.Collections.Generic;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.Fireworks;

/// <summary>
/// "Фейерверк" — ракеты стартуют снизу экрана по удару (frame.Beat),
/// поднимаются с гравитационным торможением и оставляют дымный след из
/// мелких искр, а по истечении фитиля взрываются облаком частиц,
/// разлетающихся во все стороны и гаснущих под собственной гравитацией
/// и сопротивлением воздуха.
///
/// Если бита долго нет (тихая/безритмичная запись), но звук всё же
/// идёт — держать экран пустым скучно, поэтому есть запасной таймер,
/// который сам запускает ракету через несколько секунд тишины.
///
/// Про цвет: у каждого фейерверка — свой случайный оттенок, выбранный
/// в момент запуска ракеты (и унаследованный её следом и вспышкой).
/// Отдельный дрейфующий HueState тут не нужен — сама природа плагина
/// уже гарантирует, что один и тот же цвет не будет висеть на экране
/// подолгу: он в принципе не такой.
/// </summary>
public sealed class FireworksVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "fireworks";
    public string DisplayName => "Фейерверк";

    private const int MaxConcurrentRockets = 6;
    private const int MaxSparks = 2600;

    // Защита от дребезга детектора бита (несколько кадров подряд Beat=true
    // на один и тот же удар) — без неё один удар мог бы запускать пачку ракет.
    private const float LaunchCooldownSeconds = 0.16f;

    // Если чётких битов нет дольше этого времени, но звук не тишина —
    // запускаем ракету сами, чтобы визуализация не простаивала. Значение
    // намеренно небольшое: на записях без выраженного ритма это основной
    // источник ракет, и по нему видно, что плагин живой, а не завис.
    private const float SilenceFallbackSeconds = 1.6f;
    private const float SilenceVolumeThreshold = 0.05f;

    private const float TrailSparkInterval = 0.018f;
    private const float SparkDragPerSecond = 0.5f;

    private readonly Random _random = new();
    private readonly List<Rocket> _rockets = new();
    private readonly List<Spark> _sparks = new();

    private DateTime _lastFrameTime = DateTime.UtcNow;
    private float _cooldownRemaining;
    private float _silenceTimer;

    private sealed class Rocket
    {
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Age;
        public float FuseTime;
        public float Hue;
        public float TrailAccumulator;
    }

    private sealed class Spark
    {
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Age;
        public float MaxAge;
        public float BaseSize;
        public float Hue;
        public float Saturation;
        public float Gravity;
    }

    public void Init(VisualizerContext context)
    {
        _rockets.Clear();
        _sparks.Clear();
        _lastFrameTime = DateTime.UtcNow;
        _cooldownRemaining = 0f;

        // Взводим запасной таймер сразу на порог срабатывания: при
        // переключении на плагин первая ракета уходит почти мгновенно,
        // а не только через SilenceFallbackSeconds после старта.
        _silenceTimer = SilenceFallbackSeconds;
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        DrawBackground(canvas, info);

        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        float dt = Math.Clamp((float)(now - _lastFrameTime).TotalSeconds, 0f, 0.1f);
        _lastFrameTime = now;

        float minSide = Math.Min(info.Width, info.Height);
        float volume = Math.Clamp(frame.Volume, 0f, 1f);

        _cooldownRemaining = Math.Max(0f, _cooldownRemaining - dt);
        _silenceTimer += dt;

        bool canLaunch = _rockets.Count < MaxConcurrentRockets;
        bool beatLaunch = frame.Beat && _cooldownRemaining <= 0f && canLaunch;
        bool silenceLaunch = !beatLaunch && canLaunch
            && _silenceTimer >= SilenceFallbackSeconds && volume > SilenceVolumeThreshold;

        if (beatLaunch || silenceLaunch)
        {
            int count = (beatLaunch && volume > 0.78f && _rockets.Count < MaxConcurrentRockets - 1) ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                LaunchRocket(info, minSide, volume);
            }

            _cooldownRemaining = LaunchCooldownSeconds;
            _silenceTimer = 0f;
        }

        UpdateRockets(dt, minSide);
        UpdateSparks(dt);

        DrawSparks(canvas);
        DrawRockets(canvas);
    }

    private void LaunchRocket(SKImageInfo info, float minSide, float volume)
    {
        float x = info.Width * (0.12f + (float)_random.NextDouble() * 0.76f);
        float y = info.Height;

        float speedFactor = 0.55f + (float)_random.NextDouble() * 0.3f + volume * 0.25f;

        _rockets.Add(new Rocket
        {
            X = x,
            Y = y,
            Vx = ((float)_random.NextDouble() - 0.5f) * minSide * 0.12f,
            Vy = -minSide * speedFactor,
            Age = 0f,
            FuseTime = 0.55f + (float)_random.NextDouble() * 0.45f,
            Hue = RandomHue(),
            TrailAccumulator = 0f,
        });
    }

    private void UpdateRockets(float dt, float minSide)
    {
        float gravity = minSide * 0.9f;

        for (int i = _rockets.Count - 1; i >= 0; i--)
        {
            var r = _rockets[i];
            r.Vy += gravity * dt;
            r.X += r.Vx * dt;
            r.Y += r.Vy * dt;
            r.Age += dt;

            r.TrailAccumulator += dt;
            while (r.TrailAccumulator >= TrailSparkInterval)
            {
                SpawnTrailSpark(r);
                r.TrailAccumulator -= TrailSparkInterval;
            }

            if (r.Age >= r.FuseTime || r.Y <= 0f)
            {
                Explode(r, minSide);
                _rockets.RemoveAt(i);
            }
        }
    }

    private void SpawnTrailSpark(Rocket r)
    {
        if (_sparks.Count >= MaxSparks)
        {
            return;
        }

        _sparks.Add(new Spark
        {
            X = r.X + ((float)_random.NextDouble() - 0.5f) * 3f,
            Y = r.Y,
            Vx = ((float)_random.NextDouble() - 0.5f) * 20f,
            Vy = -r.Vy * 0.08f + ((float)_random.NextDouble() - 0.5f) * 20f,
            Age = 0f,
            MaxAge = 0.22f + (float)_random.NextDouble() * 0.12f,
            BaseSize = 1.4f,
            Hue = r.Hue,
            Saturation = 25f,
            Gravity = 0f,
        });
    }

    private void Explode(Rocket r, float minSide)
    {
        int count = 70 + _random.Next(60);
        float baseSpeed = minSide * (0.22f + (float)_random.NextDouble() * 0.16f);

        for (int i = 0; i < count; i++)
        {
            if (_sparks.Count >= MaxSparks)
            {
                break;
            }

            float angle = (float)(_random.NextDouble() * Math.PI * 2.0);
            float speed = baseSpeed * (0.4f + (float)_random.NextDouble() * 0.8f);

            _sparks.Add(new Spark
            {
                X = r.X,
                Y = r.Y,
                Vx = MathF.Cos(angle) * speed,
                Vy = MathF.Sin(angle) * speed,
                Age = 0f,
                MaxAge = 0.9f + (float)_random.NextDouble() * 0.7f,
                BaseSize = 2f + (float)_random.NextDouble() * 2f,
                Hue = (r.Hue + ((float)_random.NextDouble() - 0.5f) * 26f + 360f) % 360f,
                Saturation = 55f + (float)_random.NextDouble() * 35f,
                Gravity = minSide * 0.5f,
            });
        }
    }

    private void UpdateSparks(float dt)
    {
        float dragFactor = MathF.Max(0f, 1f - SparkDragPerSecond * dt);

        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var s = _sparks[i];
            s.Vy += s.Gravity * dt;
            s.Vx *= dragFactor;
            s.Vy *= dragFactor;
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            s.Age += dt;

            if (s.Age >= s.MaxAge)
            {
                _sparks.RemoveAt(i);
            }
        }
    }

    private void DrawSparks(SKCanvas canvas)
    {
        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(3.5f, 3.5f),
        };
        using var corePaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };

        foreach (var s in _sparks)
        {
            float ageFraction = Math.Clamp(s.Age / s.MaxAge, 0f, 1f);
            float brightness = 100f - ageFraction * 55f;
            var color = SKColor.FromHsv(s.Hue, s.Saturation, brightness);

            byte alpha = (byte)Math.Clamp(255f * (1f - ageFraction), 0f, 255f);
            float size = s.BaseSize * (1f - ageFraction * 0.5f);
            if (size <= 0.05f || alpha == 0)
            {
                continue;
            }

            glowPaint.Color = color.WithAlpha((byte)(alpha * 0.45f));
            canvas.DrawCircle(s.X, s.Y, size * 2.4f, glowPaint);

            corePaint.Color = color.WithAlpha(alpha);
            canvas.DrawCircle(s.X, s.Y, size, corePaint);
        }
    }

    private void DrawRockets(SKCanvas canvas)
    {
        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(3f, 3f),
        };
        using var corePaint = new SKPaint { IsAntialias = true };

        foreach (var r in _rockets)
        {
            var glowColor = SKColor.FromHsv(r.Hue, 25f, 100f);
            glowPaint.Color = glowColor.WithAlpha(140);
            canvas.DrawCircle(r.X, r.Y, 7f, glowPaint);

            corePaint.Color = new SKColor(255, 255, 255, 235);
            canvas.DrawCircle(r.X, r.Y, 2.4f, corePaint);
        }
    }

    private float RandomHue() => (float)(_random.NextDouble() * 360.0);

    private static void DrawBackground(SKCanvas canvas, SKImageInfo info)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, info.Height),
            new[] { new SKColor(6, 8, 20), new SKColor(16, 12, 36) },
            new[] { 0f, 1f },
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), paint);
    }
}
