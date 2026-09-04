using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace MusikColor.Adapters.Visualization;

/// <summary>
/// Свой контекст загрузки на каждый DLL-плагин. Приватные зависимости
/// плагина (если они у него есть) резолвятся из его собственной папки
/// через .deps.json. А вот общие сборки — контракт (MusikColor.Contracts)
/// и SkiaSharp — намеренно НЕ грузим повторно из папки плагина: если это
/// сделать, плагин получит свою собственную копию типа IVisualizerPlugin
/// или SKCanvas, отличную от той, что использует хост, и вызовы вроде
/// "IsAssignableFrom" или прямой вызов Render(...) будут падать с
/// непонятными ошибками несовпадения типов. Поэтому для общих сборок
/// возвращаем null — тогда рантайм резолвит их из дефолтного контекста,
/// где они уже загружены хостом.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "MusikColor.Contracts",
        "SkiaSharp",
    };

    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginDllPath) : base(isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(pluginDllPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name != null && SharedAssemblyNames.Contains(assemblyName.Name))
        {
            return null;
        }

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }
}
