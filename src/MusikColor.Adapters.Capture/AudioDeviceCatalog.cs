using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace MusikColor.Adapters.Capture;

public sealed record AudioDeviceInfo(string Id, string FriendlyName);

/// <summary>Список аудио-устройств Windows для UI (выбор в выпадающем списке).</summary>
public static class AudioDeviceCatalog
{
    public static IReadOnlyList<AudioDeviceInfo> RenderDevices() => Enumerate(DataFlow.Render);

    public static IReadOnlyList<AudioDeviceInfo> CaptureDevices() => Enumerate(DataFlow.Capture);

    /// <summary>
    /// Id устройства вывода "по умолчанию" — того самого, через которое
    /// Windows сейчас реально проигрывает звук. Нужно, чтобы не заставлять
    /// пользователя вручную угадывать нужное устройство в списке.
    /// </summary>
    public static string? DefaultRenderDeviceId() => DefaultDeviceId(DataFlow.Render);

    public static string? DefaultCaptureDeviceId() => DefaultDeviceId(DataFlow.Capture);

    public static MMDevice ById(string id)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(id);
    }

    private static string? DefaultDeviceId(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.ID;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Устройства по умолчанию может не быть (например, нет ни одного
            // включённого выхода/входа) — тогда просто нечего предвыбрать.
            return null;
        }
    }

    private static IReadOnlyList<AudioDeviceInfo> Enumerate(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var list = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
            device.Dispose();
        }
        return list;
    }
}
