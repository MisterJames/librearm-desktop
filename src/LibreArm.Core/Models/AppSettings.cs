namespace LibreArm.Core.Models;

public sealed record AppSettings(string? RememberedDeviceName, string? RememberedBluetoothAddress)
{
    public bool HasRememberedDevice => !string.IsNullOrWhiteSpace(RememberedBluetoothAddress);
}
