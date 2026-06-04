namespace LibreArm_Desktop.Models;

public sealed class BleDeviceFoundEventArgs : EventArgs
{
    public BleDeviceFoundEventArgs(DiscoveredBleDevice device)
    {
        Device = device;
    }

    public DiscoveredBleDevice Device { get; }
}
