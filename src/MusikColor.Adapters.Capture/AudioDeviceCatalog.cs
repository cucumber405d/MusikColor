using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace MusikColor.Adapters.Capture;

public sealed record AudioDeviceInfo(string Id, string FriendlyName);

/// <summary>Список аудио-устройств Windows для UI (выбор в выпадающем списке).</summary>
public static class AudioDeviceCatalog
{
    public static IReadOnlyList<AudioDeviceInfo> RenderDevices() => Enumerate(DataFlow.Render);

    public static IReadOnlyList<AudioDeviceInfo> CaptureDevices() => Enumerate(DataFlow.Capture);

    public static MMDevice ById(string id)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(id);
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
