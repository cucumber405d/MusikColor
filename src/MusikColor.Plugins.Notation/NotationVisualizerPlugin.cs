using System;
using System.Collections.Generic;
using SkiaSharp;
using MusikColor.Contracts;
using MusikColor.Plugins.Shared;

namespace MusikColor.Plugins.Notation;

/// <summary>
/// "Генеративная нотация" — на фоне звёздного поля (StarFieldRenderer,
/// см. MusikColor.Plugins.Shared) плывут ноты: доминирующая частота
/// каждого спектрального кадра переводится в номер ноты по равномерно
/// темперированному строю (A4 = MIDI 69 = 440 Гц) и рисуется на нотном
/// стане, плывущем справа налево, как бегущая строка.
///
/// Размер нот и толщина линий считаются от фактического разрешения
/// холста (а не фиксированной константой в пикселях) — иначе на
/// большом экране/высоком DPI ноты выглядят мелкими и незаметными.
/// </summary>
public sealed class NotationVisualizerPlugin : IVisualizerPlugin
{
    public string Id => "notation";
    public string DisplayName => "Генеративная нотация";

    private const float NoteSpeed = 90f; // px/sec
    private const double SpawnIntervalMs = 180;
    private const float MinBandLevel = 0.15f;
    private const float MaxNoteAgeSeconds = 14f;

    // Радиус головки ноты — доля от меньшей стороны экрана, с разумными
    // пределами, чтобы на маленьком окне не исчезала, а на большом
    // экране не разрасталась карикатурно.
    private const float NoteRadiusFactor = 0.024f;
    private const float MinNoteRadius = 10f;
    private const float MaxNoteRadius = 46f;

    private readonly StarFieldRenderer _background = new();
    private readonly List<NoteEvent> _notes = new();

    private float[] _bandFrequencies = Array.Empty<float>();
    private DateTime _lastFrameTime = DateTime.UtcNow;
    private DateTime _lastSpawnTime = DateTime.MinValue;

    private sealed class NoteEvent
    {
        public float X;
        public float MidiNote;
        public SKColor Color;
        public float Age;
    }

    public void Init(VisualizerContext context)
    {
        _background.Init();
        _bandFrequencies = context.BandCenterFrequencies;
        _lastFrameTime = DateTime.UtcNow;
        _lastSpawnTime = DateTime.MinValue;
        _notes.Clear();
    }

    public void Render(SKCanvas canvas, SKImageInfo info, FrequencyFrame frame)
    {
        _background.Render(canvas, info, frame);

        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        float dt = (float)(now - _lastFrameTime).TotalSeconds;
        _lastFrameTime = now;
        dt = Math.Clamp(dt, 0f, 0.25f); // защита от рывков после паузы/сворачивания окна

        float noteRadius = ComputeNoteRadius(info);

        MaybeSpawnNote(frame, now, info, noteRadius);
        AdvanceNotes(dt, noteRadius);
        DrawStaff(canvas, info, noteRadius);
        DrawNotes(canvas, info, noteRadius);
    }

    private static float ComputeNoteRadius(SKImageInfo info)
    {
        float minSide = Math.Min(info.Width, info.Height);
        return Math.Clamp(minSide * NoteRadiusFactor, MinNoteRadius, MaxNoteRadius);
    }

    private void MaybeSpawnNote(FrequencyFrame frame, DateTime now, SKImageInfo info, float noteRadius)
    {
        if ((now - _lastSpawnTime).TotalMilliseconds < SpawnIntervalMs)
        {
            return;
        }

        var bands = frame.Bands;
        if (bands.Length == 0 || _bandFrequencies.Length != bands.Length)
        {
            return;
        }

        int bestIndex = 0;
        float bestValue = bands[0];
        for (int i = 1; i < bands.Length; i++)
        {
            if (bands[i] > bestValue)
            {
                bestValue = bands[i];
                bestIndex = i;
            }
        }

        if (bestValue < MinBandLevel)
        {
            return;
        }

        _lastSpawnTime = now;

        float freq = _bandFrequencies[bestIndex];
        float midiNote = 69f + 12f * MathF.Log2(freq / 440f);

        int thirdA = Math.Max(1, bands.Length / 3);
        int thirdB = Math.Max(thirdA + 1, bands.Length * 2 / 3);
        SKColor color = bestIndex < thirdA ? StarFieldRenderer.BassColor
            : bestIndex < thirdB ? StarFieldRenderer.MidColor
            : StarFieldRenderer.TrebleColor;

        _notes.Add(new NoteEvent
        {
            X = info.Width + noteRadius * 2f,
            MidiNote = midiNote,
            Color = color,
            Age = 0f,
        });
    }

    private void AdvanceNotes(float dt, float noteRadius)
    {
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            var note = _notes[i];
            note.X -= NoteSpeed * dt;
            note.Age += dt;

            if (note.X < -noteRadius * 2f || note.Age > MaxNoteAgeSeconds)
            {
                _notes.RemoveAt(i);
            }
        }
    }

    private void DrawStaff(SKCanvas canvas, SKImageInfo info, float noteRadius)
    {
        float top = info.Height * 0.34f;
        float spacing = info.Height * 0.045f;
        float lineWidth = Math.Clamp(noteRadius * 0.16f, 1.5f, 6f);

        using var linePaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 130),
            StrokeWidth = lineWidth,
            Style = SKPaintStyle.Stroke,
        };

        for (int i = 0; i < 5; i++)
        {
            float y = top + i * spacing;
            canvas.DrawLine(0, y, info.Width, y, linePaint);
        }
    }

    private void DrawNotes(SKCanvas canvas, SKImageInfo info, float noteRadius)
    {
        float staffTop = info.Height * 0.34f;
        float spacing = info.Height * 0.045f;
        float staffBottom = staffTop + 4 * spacing;
        float staffCenterY = staffTop + 2 * spacing; // средняя (3-я) линия
        float semitoneStep = spacing / 2f;

        // Референс: MIDI 71 = B4, средняя линия скрипичного ключа.
        const float ReferenceMidi = 71f;

        float stemWidth = Math.Clamp(noteRadius * 0.26f, 2.5f, 8f);
        float ledgerWidth = Math.Clamp(noteRadius * 0.22f, 2f, 7f);
        float stemLength = spacing * 1.8f + noteRadius * 0.6f;

        using var headPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var headOutlinePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Clamp(noteRadius * 0.14f, 1f, 4f),
            Color = new SKColor(10, 10, 16, 220),
        };
        using var stemPaint = new SKPaint { IsAntialias = true, StrokeWidth = stemWidth, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        using var ledgerPaint = new SKPaint
        {
            IsAntialias = true,
            StrokeWidth = ledgerWidth,
            Style = SKPaintStyle.Stroke,
        };

        foreach (var note in _notes)
        {
            float rawY = staffCenterY - (note.MidiNote - ReferenceMidi) * semitoneStep;

            // Реальный диапазон частот (~30 Гц..16 кГц) даёт около 100
            // полутонов — без ограничения добавочные линейки улетали бы
            // на сотни пикселей за пределы экрана. Прижимаем итоговую Y
            // к разумному запасу над/под станом (не больше ~3 линеек),
            // сохраняя пропорциональность для нот у середины диапазона.
            float y = Math.Clamp(rawY, staffTop - spacing * 3f, staffBottom + spacing * 3f);

            float ageFactor = 1f - Math.Clamp(note.Age / MaxNoteAgeSeconds, 0f, 1f);
            byte alpha = (byte)Math.Clamp(60f + 195f * ageFactor, 0f, 255f);

            // Добавочные линейки сверху/снизу стана.
            if (y < staffTop - spacing * 0.5f)
            {
                ledgerPaint.Color = new SKColor(255, 255, 255, alpha);
                for (float ly = staffTop - spacing; ly >= y - spacing * 0.5f; ly -= spacing)
                {
                    canvas.DrawLine(note.X - noteRadius * 1.6f, ly, note.X + noteRadius * 1.6f, ly, ledgerPaint);
                }
            }
            else if (y > staffBottom + spacing * 0.5f)
            {
                ledgerPaint.Color = new SKColor(255, 255, 255, alpha);
                for (float ly = staffBottom + spacing; ly <= y + spacing * 0.5f; ly += spacing)
                {
                    canvas.DrawLine(note.X - noteRadius * 1.6f, ly, note.X + noteRadius * 1.6f, ly, ledgerPaint);
                }
            }

            stemPaint.Color = note.Color.WithAlpha(alpha);
            canvas.DrawLine(note.X + noteRadius * 0.9f, y, note.X + noteRadius * 0.9f, y - stemLength, stemPaint);

            var headRect = new SKRect(-noteRadius, -noteRadius * 0.72f, noteRadius, noteRadius * 0.72f);

            headPaint.Color = note.Color.WithAlpha(alpha);
            headOutlinePaint.Color = new SKColor(10, 10, 16, alpha);

            canvas.Save();
            canvas.Translate(note.X, y);
            canvas.RotateDegrees(-20);
            canvas.DrawOval(headRect, headPaint);
            canvas.DrawOval(headRect, headOutlinePaint);
            canvas.Restore();
        }
    }
}
