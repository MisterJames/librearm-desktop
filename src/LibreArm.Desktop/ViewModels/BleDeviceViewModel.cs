namespace LibreArm_Desktop.ViewModels;

using LibreArm_Desktop.Models;

public sealed class BleDeviceViewModel : ObservableObject
{
    private string _name;
    private bool _hasBloodPressureService;
    private short _rssi;
    private int _priority;

    public BleDeviceViewModel(DiscoveredBleDevice device)
    {
        BluetoothAddress = device.BluetoothAddress;
        BluetoothAddressText = device.BluetoothAddressText;
        _name = device.Name;
        _hasBloodPressureService = device.HasBloodPressureService;
        _rssi = device.RawSignalStrengthInDBm;
        _priority = device.Priority;
    }

    public ulong BluetoothAddress { get; }

    public string BluetoothAddressText { get; }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public bool HasBloodPressureService
    {
        get => _hasBloodPressureService;
        private set
        {
            if (SetProperty(ref _hasBloodPressureService, value))
            {
                OnPropertyChanged(nameof(ServiceText));
            }
        }
    }

    public short Rssi
    {
        get => _rssi;
        private set
        {
            if (SetProperty(ref _rssi, value))
            {
                OnPropertyChanged(nameof(RssiText));
            }
        }
    }

    public int Priority
    {
        get => _priority;
        private set => SetProperty(ref _priority, value);
    }

    public string ServiceText => HasBloodPressureService ? "Blood Pressure service" : "BLE advertisement";

    public string RssiText => $"{Rssi} dBm";

    public bool IsPrioritized => HasBloodPressureService || Name.Contains("Qardio", StringComparison.OrdinalIgnoreCase);

    public DiscoveredBleDevice ToDiscoveredDevice()
    {
        return new DiscoveredBleDevice(BluetoothAddress, Name, HasBloodPressureService, Rssi);
    }

    public void Update(DiscoveredBleDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.Name) && !device.Name.StartsWith("BLE ", StringComparison.Ordinal))
        {
            Name = device.Name;
        }

        HasBloodPressureService = HasBloodPressureService || device.HasBloodPressureService;
        Rssi = device.RawSignalStrengthInDBm;
        Priority = device.Priority;
        OnPropertyChanged(nameof(IsPrioritized));
    }
}
