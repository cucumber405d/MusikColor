# MusikColor

Windows-приложение для визуализации музыки: перехват звука (системный вывод
через WASAPI loopback или микрофон) → анализ спектра → цветомузыка /
частотные бары / любые другие плагины визуализации.

## Архитектура

Проект построен по схеме "порты и адаптеры" (гексагональная архитектура):
ядро ничего не знает о WASAPI, WPF или SkiaSharp напрямую — оно общается
с внешним миром только через интерфейсы из `MusikColor.Contracts`.

```
                 IAudioSource                          IVisualizationSink
   (WASAPI-адаптер) ---> [ MusikColor.Core ] ---> (адаптер визуализации)
   LoopbackAudioSource      SpectrumEngine           VisualizationHost
   MicrophoneAudioSource    (FFT, банды,                    |
                             нормализация,                  v
                             AGC, бит-детект)          IVisualizerPlugin
                                                    (Bars, ColorField, ...)
```

Проекты:

- **MusikColor.Contracts** — общий контракт: `AudioFrame`, `FrequencyFrame`,
  порты `IAudioSource` / `IVisualizationSink`, интерфейс плагина
  `IVisualizerPlugin`. Ни от кого не зависит, кроме SkiaSharp (тип холста
  для рисования).
- **MusikColor.Core** — вся математика: скользящее окно сэмплов, FFT
  (простой radix-2, без внешних DSP-библиотек), группировка бинов в
  логарифмические частотные полосы, нормализация с авто-усилением (AGC) и
  плавным затуханием, простой бит-детект. На выходе — `FrequencyFrame`
  с массивом `Bands` в диапазоне [0..1].
- **MusikColor.Adapters.Capture** — реализация `IAudioSource` поверх NAudio:
  `LoopbackAudioSource` (WASAPI loopback — перехват того, что играет через
  колонки, без виртуальных кабелей) и `MicrophoneAudioSource` (обычный вход).
  Плюс `AudioDeviceCatalog` для списка устройств в UI.
- **MusikColor.Adapters.Visualization** — `VisualizationHost` (реализация
  `IVisualizationSink`, хранит последний кадр для UI) и `PluginLoader` —
  честная динамическая загрузка плагинов из папки `Plugins` рядом с exe
  через `AssemblyLoadContext` (плагин можно добавить, просто положив DLL
  в эту папку, без пересборки приложения).
- **MusikColor.Plugins.Bars** / **MusikColor.Plugins.ColorField** — два
  готовых плагина: классические частотные бары с радугой по частоте и
  "цветомузыка" в духе советских самодельных наборов: три канала на
  реальных частотных полосах (бас -> красный, середина -> жёлтый, верха
  -> зелёный), синий = тишина, общее число "лампочек" на экране постоянно
  -- меняется только их цветовой состав и положение.
- **MusikColor.App** — WPF-приложение: окно с выбором источника звука,
  устройства и визуализации, `SKElement` (SkiaSharp) как холст, рендер-цикл
  на `CompositionTarget.Rendering` (~60 fps, независимо от частоты
  аудио-коллбэков).

## Как собрать

Нужны: Visual Studio 2022 (17.8+) с workload **.NET desktop development**,
либо .NET 8 SDK + `dotnet build` (сама WPF-сборка требует Windows — на
Linux/macOS `MusikColor.App` не соберётся, остальные проекты — соберутся).

1. Откройте `MusikColor.sln`.
2. Дайте восстановиться NuGet-пакетам (NAudio, SkiaSharp, SkiaSharp.Views.WPF).
3. Поставьте `MusikColor.App` как стартовый проект и запустите (F5).
4. В окне выберите источник (системный звук или микрофон), устройство,
   визуализацию — и нажмите "Старт".

## Как добавить свой плагин визуализации

1. Новый проект `classlib` (`net8.0`), ссылка на `MusikColor.Contracts`.
2. Реализовать `IVisualizerPlugin`: `Id`, `DisplayName`, `Init(context)`,
   `Render(canvas, info, frame)` — рисуете через обычный SkiaSharp API.
3. Либо указать в `.csproj` такой же `OutputPath` в `Plugins`-папку хоста,
   как у `MusikColor.Plugins.Bars`, либо просто скопировать собранную DLL
   (вместе с `.deps.json`) в `MusikColor.App/bin/<Config>/net8.0-windows/Plugins/`.
4. При следующем запуске плагин появится в выпадающем списке сам.

## Важно: код не проверялся компиляцией

Я собирал этот проект в среде без установленного .NET SDK и без Windows,
поэтому "живой" сборки и запуска не было — весь код написан аккуратно и
по хорошо документированным API (WASAPI через NAudio, SkiaSharp для
рендера), но мелкие ошибки на первой сборке вполне возможны (несовпадение
версии SDK, что-то в XAML и т.п.). Если Visual Studio что-то подсветит —
кидайте текст ошибки, поправим быстро.

## Дальнейшие идеи

- Больше плагинов: осциллограф, "плазма"/kaleidoscope на шейдерах,
  частицы, реакция на удары (бас-пульс), пресеты с переходами.
- Полноэкранный режим / вывод на второй монитор.
- Настройки чувствительности (gain, скорость затухания) прямо из UI —
  сейчас константы зашиты в `Normalizer`.
- Если захочется MilkDrop-уровня эффектов — не писать шейдерный движок
  с нуля, а встроить открытый **projectM** (клон MilkDrop) и просто
  скармливать ему `FrequencyFrame`/сырой сигнал.
