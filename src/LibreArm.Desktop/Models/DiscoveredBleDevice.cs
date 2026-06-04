namespace LibreArm_Desktop.Models;

public sealed record DiscoveredBleDevice(
    ulong BluetoothAddress,
    string Name,
    bool HasBloodPressureService,
    short RawSignalStrengthInDBm)
{
    public bool LooksLikeQardio => Name.Contains("Qardio", StringComparison.OrdinalIgnoreCase);

    public int Priority => (LooksLikeQardio ? 1000 : 0) + (HasBloodPressureService ? 500 : 0) + RawSignalStrengthInDBm;

    public string BluetoothAddressText => BluetoothAddress.ToString("X12");
}
