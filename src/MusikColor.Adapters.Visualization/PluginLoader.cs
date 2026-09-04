using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MusikColor.Contracts;

namespace MusikColor.Adapters.Visualization;

/// <summary>
/// Находит и загружает плагины визуализации: любая .dll в папке PluginsPath,
/// содержащая публичный класс с реализацией IVisualizerPlugin, подхватывается
/// автоматически — пересборка хоста не нужна, достаточно положить файл рядом.
/// </summary>
public sealed class PluginLoader
{
    private readonly string _pluginsPath;

    public PluginLoader(string pluginsPath)
    {
        _pluginsPath = pluginsPath;
    }

    public IReadOnlyList<IVisualizerPlugin> LoadAll()
    {
        var result = new List<IVisualizerPlugin>();

        if (!Directory.Exists(_pluginsPath))
        {
            return result;
        }

        foreach (var dllPath in Directory.EnumerateFiles(_pluginsPath, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var context = new PluginLoadContext(dllPath);
                var assembly = context.LoadFromAssemblyPath(dllPath);

                var pluginTypes = assembly.GetTypes()
                    .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IVisualizerPlugin).IsAssignableFrom(t));

                foreach (var type in pluginTypes)
                {
                    if (Activator.CreateInstance(type) is IVisualizerPlugin plugin)
                    {
                        result.Add(plugin);
                    }
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or ReflectionTypeLoadException or FileLoadException)
            {
                // DLL в папке плагинов, которая не является совместимой сборкой — просто пропускаем.
            }
        }

        return result;
    }
}
