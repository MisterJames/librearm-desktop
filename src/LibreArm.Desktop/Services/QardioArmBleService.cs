namespace LibreArm_Desktop.Services;

using LibreArm.Core.Bluetooth;
using LibreArm.Core.Models;
using LibreArm_Desktop.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

public sealed class QardioArmBleService : IDisposable
{
    private static readonly TimeSpan StartCommandCooldown = TimeSpan.FromSeconds(8);
    private BluetoothLEAdvertisementWatcher? _watcher;
    private BluetoothLEDevice? _device;
    private GattDeviceService? _bloodPressureService;
    private GattCharacteristic? _measurementCharacteristic;
    private GattCharacteristic? _controlCharacteristic;
    private DateTimeOffset _lastStartAttempt = DateTimeOffset.MinValue;
    private bool _measurementInProgress;

    public event EventHandler<BleDeviceFoundEventArgs>? DeviceFound;
    public event EventHandler<BloodPressureReading>? ReadingReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<int?>? BatteryLevelChanged;

    public bool IsConnected => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;

    public string ConnectedDeviceName => _device?.Name ?? "";

    public string ConnectedBluetoothAddress => _device?.BluetoothAddress.ToString("X12") ?? "";

    public void StartScanning(bool active = true)
    {
        StopScanning();

        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = active ? BluetoothLEScanningMode.Active : BluetoothLEScanningMode.Passive
        };
        _watcher.Received += OnAdvertisementReceived;
        _watcher.Stopped += OnWatcherStopped;
        _watcher.Start();
        RaiseStatus(active ? "Scanning for BLE devices." : "Watching for remembered QardioArm.");
    }

    public void StopScanning()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.Received -= OnAdvertisementReceived;
        _watcher.Stopped -= OnWatcherStopped;
        if (_watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started or BluetoothLEAdvertisementWatcherStatus.Created)
        {
            _watcher.Stop();
        }

        _watcher = null;
    }

    public async Task ConnectAsync(DiscoveredBleDevice device)
    {
        await DisconnectAsync();
        RaiseStatus($"Connecting to {device.Name}...");

        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(device.BluetoothAddress);
        if (_device is null)
        {
            throw new InvalidOperationException("Windows could not open the BLE device.");
        }

        _device.ConnectionStatusChanged += OnConnectionStatusChanged;

        _bloodPressureService = await GetRequiredServiceAsync(GattUuids.BloodPressureService, "Blood Pressure service");
        _measurementCharacteristic = await GetRequiredCharacteristicAsync(
            _bloodPressureService,
            GattUuids.BloodPressureMeasurementCharacteristic,
            "Blood Pressure Measurement characteristic");

        _controlCharacteristic = await FindCharacteristicAsync(GattUuids.QardioControlCharacteristic);
        if (_controlCharacteristic is null)
        {
            throw new InvalidOperationException("Qardio control characteristic was not found.");
        }

        await SubscribeToMeasurementsAsync();
        await ReadBatteryLevelAsync();

        ConnectionChanged?.Invoke(this, IsConnected);
        RaiseStatus($"Connected to {_device.Name}.");
    }

    public async Task DisconnectAsync()
    {
        StopScanning();
        _measurementInProgress = false;

        if (_measurementCharacteristic is not null)
        {
            _measurementCharacteristic.ValueChanged -= OnMeasurementValueChanged;
            try
            {
                await _measurementCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch
            {
                // Keep disconnect best-effort; the app should not get stuck here.
            }
        }

        if (_device is not null)
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }

        _measurementCharacteristic = null;
        _controlCharacteristic = null;
        _bloodPressureService?.Dispose();
        _bloodPressureService = null;
        _device?.Dispose();
        _device = null;

        BatteryLevelChanged?.Invoke(this, null);
        ConnectionChanged?.Invoke(this, false);
        RaiseStatus("Disconnected.");
    }

    public async Task StartMeasurementAsync()
    {
        if (_measurementInProgress)
        {
            RaiseStatus("A measurement is already in progress.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastStartAttempt < StartCommandCooldown)
        {
            RaiseStatus("Start command ignored to avoid repeated measurement starts.");
            return;
        }

        _lastStartAttempt = now;
        await WriteControlCommandAsync([0xF1, 0x01], "start measurement");
        _measurementInProgress = true;
    }

    public async Task StopMeasurementAsync()
    {
        await WriteControlCommandAsync([0xF1, 0x02], "stop measurement");
        _measurementInProgress = false;
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var name = string.IsNullOrWhiteSpace(args.Advertisement.LocalName)
            ? $"BLE {args.BluetoothAddress:X12}"
            : args.Advertisement.LocalName;
        var hasBloodPressureService = args.Advertisement.ServiceUuids.Contains(GattUuids.BloodPressureService);
        DeviceFound?.Invoke(this, new BleDeviceFoundEventArgs(new DiscoveredBleDevice(
            args.BluetoothAddress,
            name,
            hasBloodPressureService,
            args.RawSignalStrengthInDBm)));
    }

    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (args.Error != BluetoothError.Success)
        {
            RaiseStatus($"BLE scan stopped: {args.Error}.");
        }
    }

    private async Task<GattDeviceService> GetRequiredServiceAsync(Guid serviceUuid, string label)
    {
        if (_device is null)
        {
            throw new InvalidOperationException("No BLE device is connected.");
        }

        var result = await _device.GetGattServicesForUuidAsync(serviceUuid, BluetoothCacheMode.Uncached);
        if (result.Status != GattCommunicationStatus.Success || result.Services.Count == 0)
        {
            throw new InvalidOperationException($"{label} was not found. Status: {result.Status}.");
        }

        return result.Services[0];
    }

    private static async Task<GattCharacteristic> GetRequiredCharacteristicAsync(GattDeviceService service, Guid characteristicUuid, string label)
    {
        var result = await service.GetCharacteristicsForUuidAsync(characteristicUuid, BluetoothCacheMode.Uncached);
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
        {
            throw new InvalidOperationException($"{label} was not found. Status: {result.Status}.");
        }

        return result.Characteristics[0];
    }

    private async Task<GattCharacteristic?> FindCharacteristicAsync(Guid characteristicUuid)
    {
        if (_device is null)
        {
            return null;
        }

        var serviceResult = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        if (serviceResult.Status != GattCommunicationStatus.Success)
        {
            RaiseStatus($"Could not enumerate GATT services: {serviceResult.Status}.");
            return null;
        }

        foreach (var service in serviceResult.Services)
        {
            var characteristicResult = await service.GetCharacteristicsForUuidAsync(characteristicUuid, BluetoothCacheMode.Uncached);
            if (characteristicResult.Status == GattCommunicationStatus.Success && characteristicResult.Characteristics.Count > 0)
            {
                return characteristicResult.Characteristics[0];
            }
        }

        return null;
    }

    private async Task SubscribeToMeasurementsAsync()
    {
        if (_measurementCharacteristic is null)
        {
            throw new InvalidOperationException("Blood Pressure Measurement characteristic is missing.");
        }

        var properties = _measurementCharacteristic.CharacteristicProperties;
        var cccd = properties.HasFlag(GattCharacteristicProperties.Notify)
            ? GattClientCharacteristicConfigurationDescriptorValue.Notify
            : properties.HasFlag(GattCharacteristicProperties.Indicate)
                ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
                : throw new InvalidOperationException("Blood Pressure Measurement characteristic does not support notify or indicate.");

        _measurementCharacteristic.ValueChanged += OnMeasurementValueChanged;
        var status = await _measurementCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccd);
        if (status != GattCommunicationStatus.Success)
        {
            _measurementCharacteristic.ValueChanged -= OnMeasurementValueChanged;
            throw new InvalidOperationException($"Could not subscribe to measurements. Status: {status}.");
        }
    }

    private async Task ReadBatteryLevelAsync()
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            var batteryServiceResult = await _device.GetGattServicesForUuidAsync(GattUuids.BatteryService, BluetoothCacheMode.Uncached);
            if (batteryServiceResult.Status != GattCommunicationStatus.Success || batteryServiceResult.Services.Count == 0)
            {
                BatteryLevelChanged?.Invoke(this, null);
                return;
            }

            var batteryCharacteristic = await GetRequiredCharacteristicAsync(
                batteryServiceResult.Services[0],
                GattUuids.BatteryLevelCharacteristic,
                "Battery Level characteristic");
            var readResult = await batteryCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
            if (readResult.Status != GattCommunicationStatus.Success || readResult.Value.Length == 0)
            {
                BatteryLevelChanged?.Invoke(this, null);
                return;
            }

            var reader = DataReader.FromBuffer(readResult.Value);
            BatteryLevelChanged?.Invoke(this, reader.ReadByte());
        }
        catch (Exception ex)
        {
            BatteryLevelChanged?.Invoke(this, null);
            RaiseStatus($"Battery read failed: {ex.Message}");
        }
    }

    private async Task WriteControlCommandAsync(byte[] command, string description)
    {
        if (_controlCharacteristic is null)
        {
            throw new InvalidOperationException("Qardio control characteristic is not available.");
        }

        using var writer = new DataWriter();
        writer.WriteBytes(command);
        var option = _controlCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            && !_controlCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                ? GattWriteOption.WriteWithoutResponse
                : GattWriteOption.WriteWithResponse;

        var result = await _controlCharacteristic.WriteValueWithResultAsync(writer.DetachBuffer(), option);
        if (result.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Could not {description}. Status: {result.Status}.");
        }

        RaiseStatus($"Sent {description} command.");
    }

    private void OnMeasurementValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var data = new byte[args.CharacteristicValue.Length];
            DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);
            var reading = BloodPressureMeasurementParser.Parse(data);
            _measurementInProgress = false;
            ReadingReceived?.Invoke(this, reading);
        }
        catch (Exception ex)
        {
            RaiseStatus($"Measurement parse failed: {ex.Message}");
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        var connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
        if (!connected)
        {
            _measurementInProgress = false;
        }

        ConnectionChanged?.Invoke(this, connected);
        RaiseStatus(connected ? "Device connected." : "Device disconnected.");
    }

    private void RaiseStatus(string message)
    {
        StatusChanged?.Invoke(this, message);
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }
}
