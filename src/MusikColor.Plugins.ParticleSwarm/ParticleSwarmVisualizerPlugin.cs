using System;
using System.Collections.Generic;
using SkiaSharp;
using MusikColor.Contracts;

namespace MusikColor.Plugins.ParticleSwarm;

/// <summary>
/// "Рой частиц" — каждая частотная полоса это отдельный фонтан искр
/// снизу экрана: чем громче полоса, тем чаще вылетают частицы и тем
/// выше и быстрее они летят. Частицы живут своей маленькой физикой
/// (гравитация тянет вниз, лёгкий воздушный снос по горизонтали) и
/// гаснут естественно, а не пропадают резко.
///
/// Цвет — не по полосе и не по времени, а по возрасту самой частицы:
/// огненный градиент от бело-жёлтого (свежая искра) через оранжевый к
/// тёмно-красному тлеющему угольку (перед тем как погаснуть). Такой
/// цветовой механики — "цвет как функция возраста объекта" — в проекте
/// ещё не было.
/// </summary>
public sealed class ParticleSwarmVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "particle-swarm";
    public string DisplayName => "Рой частиц";

    private const float AttackSmoothing = 0.5f;
    private const float ReleaseSmoothing = 0.08f;

    private const float SpawnRatePerSecond = 22f; // при уровне полосы = 1.0
    private const int MaxParticles = 2200;
    private const float Gravity = 260f;      // px/сек^2, тянет вниз
    private const float DragPerSecond = 0.6f; // затухание горизонтального сноса

    private static readonly (float Pos, SKColor Color)[] FireStops =
    {
        (0.00f, new SKColor(255, 255, 225)),
        (0.25f, new SKColor(255, 210, 80)),
        (0.55f, new SKColor(255, 120, 30)),
        (0.85f, new SKColor(200, 40, 15)),
        (1.00f, new SKColor(60, 10, 5)),
    };

    private readonly Random _random = new();
    private readonly List<Particle> _particles = new();

    private float[] _smoothed = Array.Empty<float>();
    private float[] _spawnAccumulator = Array.Empty<float>();
    private DateTime _lastFrameTime = DateTime.UtcNow;

    private sealed class Particle
    {
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Age;
        public float MaxAge;
        public float BaseSize;
    }

    public void Init(VisualizerContext context)
    {
        int n = Math.Max(1, context.BandCount);
        _smoothed = new float[n];
        _spawnAccumulator = new float[n];
        _particles.Clear();
        _lastFrameTime = DateTime.UtcNow;
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        DrawBackground(canvas, info);

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

        if (_smoothed.Length != n)
        {
            _smoothed = new float[n];
            _spawnAccumulator = new float[n];
        }

        var now = DateTime.UtcNow;
        float dt = Math.Clamp((float)(now - _lastFrameTime).TotalSeconds, 0f, 0.1f);
        _lastFrameTime = now;

        for (int i = 0; i < n; i++)
        {
            float target = Math.Clamp(bands[i], 0f, 1f);
            float smoothing = target > _smoothed[i] ? AttackSmoothing : ReleaseSmoothing;
            _smoothed[i] += (target - _smoothed[i]) * smoothing;
        }

        float slot = (float)info.Width / n;
        float baseline = info.Height * 0.98f;

        for (int i = 0; i < n; i++)
        {
            float level = _smoothed[i];
            if (level <= 0.01f)
            {
                continue;
            }

            _spawnAccumulator[i] += level * SpawnRatePerSecond * dt;
            while (_spawnAccumulator[i] >= 1f && _particles.Count < MaxParticles)
            {
                SpawnParticle(i, slot, baseline, level);
                _spawnAccumulator[i] -= 1f;
            }
        }

        UpdateParticles(dt);
        DrawParticles(canvas);
    }

    private void SpawnParticle(int bandIndex, float slot, float baseline, float level)
    {
        float columnCenter = (bandIndex + 0.5f) * slot;
        float spread = slot * 0.6f;

        _particles.Add(new Particle
        {
            X = columnCenter + ((float)_random.NextDouble() - 0.5f) * spread,
            Y = baseline,
            Vx = ((float)_random.NextDouble() - 0.5f) * 60f,
            Vy = -(140f + level * 520f) * (0.7f + (float)_random.NextDouble() * 0.6f),
            Age = 0f,
            MaxAge = 1.0f + (float)_random.NextDouble() * 0.9f,
            BaseSize = 2f + level * 4.5f,
        });
    }

    private void UpdateParticles(float dt)
    {
        float dragFactor = MathF.Max(0f, 1f - DragPerSecond * dt);

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Vy += Gravity * dt;
            p.Vx *= dragFactor;
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.Age += dt;

            if (p.Age >= p.MaxAge)
            {
                _particles.RemoveAt(i);
            }
        }
    }

    private void DrawParticles(SKCanvas canvas)
    {
        using var glowPaint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f),
        };
        using var corePaint = new SKPaint { IsAntialias = true };

        foreach (var p in _particles)
        {
            float ageFraction = Math.Clamp(p.Age / p.MaxAge, 0f, 1f);
            var color = FireColorFor(ageFraction);
            byte alpha = (byte)Math.Clamp(255f * (1f - ageFraction * 1.05f), 0f, 255f);
            float size = p.BaseSize * (1f - ageFraction * 0.4f);

            glowPaint.Color = color.WithAlpha((byte)(alpha * 0.5f));
            canvas.DrawCircle(p.X, p.Y, size * 2.2f, glowPaint);

            corePaint.Color = color.WithAlpha(alpha);
            canvas.DrawCircle(p.X, p.Y, size, corePaint);
        }
    }

    private static SKColor FireColorFor(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        for (int i = 0; i < FireStops.Length - 1; i++)
        {
            if (t <= FireStops[i + 1].Pos)
            {
                float span = FireStops[i + 1].Pos - FireStops[i].Pos;
                float localT = span > 0f ? (t - FireStops[i].Pos) / span : 0f;
                return LerpColor(FireStops[i].Color, FireStops[i + 1].Color, localT);
            }
        }
        return FireStops[^1].Color;
    }

    private static SKColor LerpColor(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte r = (byte)(a.Red + (b.Red - a.Red) * t);
        byte g = (byte)(a.Green + (b.Green - a.Green) * t);
        byte bl = (byte)(a.Blue + (b.Blue - a.Blue) * t);
        return new SKColor(r, g, bl);
    }

    private static void DrawBackground(SKCanvas canvas, SKImageInfo info)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, info.Height),
            new[] { new SKColor(2, 2, 4), new SKColor(18, 8, 6) },
            new[] { 0f, 1f },
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), paint);
    }
}
