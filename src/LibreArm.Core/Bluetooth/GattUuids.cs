namespace LibreArm.Core.Bluetooth;

public static class GattUuids
{
    public static readonly Guid BloodPressureService = new("00001810-0000-1000-8000-00805f9b34fb");
    public static readonly Guid BloodPressureMeasurementCharacteristic = new("00002A35-0000-1000-8000-00805f9b34fb");
    public static readonly Guid BatteryService = new("0000180F-0000-1000-8000-00805f9b34fb");
    public static readonly Guid BatteryLevelCharacteristic = new("00002A19-0000-1000-8000-00805f9b34fb");
    public static readonly Guid QardioControlCharacteristic = new("583CB5B3-875D-40ED-9098-C39EB0C1983D");
}
