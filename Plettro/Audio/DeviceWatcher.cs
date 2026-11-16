using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Plettro.Audio;

/// <summary>
/// Notifies when audio devices are plugged in, unplugged, enabled or disabled
/// (e.g. the Scarlett connected after Plettro started). Callbacks arrive on a COM
/// thread: the handler must only schedule work, never call back into Core Audio.
/// </summary>
public sealed class DeviceWatcher : IMMNotificationClient
{
    readonly Action changed;

    public DeviceWatcher(Action changed) { this.changed = changed; }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => changed();
    public void OnDeviceAdded(string pwstrDeviceId) => changed();
    public void OnDeviceRemoved(string deviceId) => changed();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
