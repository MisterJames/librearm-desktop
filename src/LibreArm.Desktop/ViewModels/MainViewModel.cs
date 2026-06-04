namespace LibreArm_Desktop.ViewModels;

using System.Collections.ObjectModel;
using LibreArm.Core.Models;
using LibreArm.Core.Services;
using LibreArm_Desktop.Models;
using LibreArm_Desktop.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Storage;

public sealed class MainViewModel : ObservableObject
{
    private static readonly TimeSpan FinalReadingQuietPeriod = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan ReadingTimeout = TimeSpan.FromSeconds(90);
    private readonly DispatcherQueue _dispatcher;
    private readonly Window _owner;
    private readonly QardioArmBleService _bleService;
    private readonly ReadingStore _store;
    private readonly CsvExportService _csvExportService;
    private readonly ProfilePhotoService _profilePhotoService;
    private readonly List<BleDeviceViewModel> _allDevices = [];
    private BleDeviceViewModel? _selectedDevice;
    private UserProfile? _activeProfile;
    private UserProfile? _selectedProfile;
    private AppSettings _settings = new(null, null);
    private CancellationTokenSource? _finalReadingDelay;
    private TaskCompletionSource<ReadingCaptureResult>? _captureCompletion;
    private BloodPressureReading? _pendingReading;
    private int _pendingSampleCount;
    private ulong? _autoConnectAddress;
    private ulong? _rememberedWatchAddress;
    private DateTimeOffset _nextWatchConnectAttempt = DateTimeOffset.MinValue;
    private int _watchFailureCount;
    private bool _watchConnectInProgress;
    private bool _isRememberedDeviceWatchRunning;
    private string _statusText = "Ready.";
    private string _connectionText = "Disconnected";
    private string _batteryText = "Battery: not available";
    private string _activeProfileText = "No profile selected";
    private bool _showAllDevices;
    private bool _isScanning;
    private bool _isConnected;
    private bool _isMeasurementRunning;
    private bool _isSessionRunning;
    private string _lastSystolicText = "--";
    private string _lastDiastolicText = "--";
    private string _lastMeanArterialPressureText = "--";
    private string _lastPulseText = "--";
    private string _lastMeasuredAtText = "--";
    private string _lastStatusText = "--";
    private string _latestCategoryText = "No readings";
    private string _latestTargetText = "AHA adult reference: below 120/80";
    private string _profileContextText = "Select a profile to show age and biological sex.";
    private string _latestDisclaimerText = "LibreArm is not medical advice.";

    public MainViewModel(DispatcherQueue dispatcher, Window owner)
    {
        _dispatcher = dispatcher;
        _owner = owner;
        _bleService = new QardioArmBleService();
        _store = new ReadingStore(Path.Combine(ApplicationData.Current.LocalFolder.Path, "librearm-readings.db"));
        _csvExportService = new CsvExportService();
        _profilePhotoService = new ProfilePhotoService();

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        StopScanCommand = new AsyncRelayCommand(StopScanAsync, () => IsScanning);
        ConnectCommand = new AsyncRelayCommand(ConnectSelectedDeviceAsync, () => SelectedDevice is not null && !IsConnected);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        StopCommand = new AsyncRelayCommand(StopMeasurementAsync, () => IsConnected);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => ActiveProfile is not null && History.Count > 0);
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync, () => ActiveProfile is not null && History.Count > 0);

        _bleService.DeviceFound += OnDeviceFound;
        _bleService.StatusChanged += (_, message) => Enqueue(() => StatusText = message);
        _bleService.ConnectionChanged += (_, connected) => Enqueue(() => SetConnected(connected));
        _bleService.BatteryLevelChanged += (_, level) => Enqueue(() => BatteryText = level is null ? "Battery: not available" : $"Battery: {level}%");
        _bleService.ReadingReceived += OnReadingReceived;
    }

    public event EventHandler? RememberedDeviceConnected;

    public ObservableCollection<BleDeviceViewModel> Devices { get; } = [];

    public ObservableCollection<MeasurementSessionItem> History { get; } = [];

    public ObservableCollection<WeeklyBloodPressureSummary> WeeklySummaries { get; } = [];

    public ObservableCollection<UserProfile> Profiles { get; } = [];

    public AppSettings Settings => _settings;

    public bool IsRememberedDeviceWatchRunning => _isRememberedDeviceWatchRunning;

    public UserProfile? ActiveProfile
    {
        get => _activeProfile;
        private set
        {
            if (SetProperty(ref _activeProfile, value))
            {
                ActiveProfileText = value is null ? "No profile selected" : $"Profile: {value.DisplayName}";
                ProfileContextText = value is null ? "Select a profile to show age and biological sex." : value.DemographicsText;
                RaiseCommandStates();
            }
        }
    }

    public UserProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => SetProperty(ref _selectedProfile, value);
    }

    public bool ShowAllDevices
    {
        get => _showAllDevices;
        set
        {
            if (SetProperty(ref _showAllDevices, value))
            {
                RefreshDisplayedDevices();
            }
        }
    }

    public BleDeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string ConnectionText
    {
        get => _connectionText;
        set => SetProperty(ref _connectionText, value);
    }

    public string BatteryText
    {
        get => _batteryText;
        set => SetProperty(ref _batteryText, value);
    }

    public string ActiveProfileText
    {
        get => _activeProfileText;
        private set => SetProperty(ref _activeProfileText, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsMeasurementRunning
    {
        get => _isMeasurementRunning;
        set
        {
            if (SetProperty(ref _isMeasurementRunning, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsSessionRunning
    {
        get => _isSessionRunning;
        private set
        {
            if (SetProperty(ref _isSessionRunning, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string LastSystolicText
    {
        get => _lastSystolicText;
        set => SetProperty(ref _lastSystolicText, value);
    }

    public string LastDiastolicText
    {
        get => _lastDiastolicText;
        set => SetProperty(ref _lastDiastolicText, value);
    }

    public string LastMeanArterialPressureText
    {
        get => _lastMeanArterialPressureText;
        set => SetProperty(ref _lastMeanArterialPressureText, value);
    }

    public string LastPulseText
    {
        get => _lastPulseText;
        set => SetProperty(ref _lastPulseText, value);
    }

    public string LastMeasuredAtText
    {
        get => _lastMeasuredAtText;
        set => SetProperty(ref _lastMeasuredAtText, value);
    }

    public string LastStatusText
    {
        get => _lastStatusText;
        set => SetProperty(ref _lastStatusText, value);
    }

    public string LatestCategoryText
    {
        get => _latestCategoryText;
        set => SetProperty(ref _latestCategoryText, value);
    }

    public string LatestTargetText
    {
        get => _latestTargetText;
        set => SetProperty(ref _latestTargetText, value);
    }

    public string ProfileContextText
    {
        get => _profileContextText;
        set => SetProperty(ref _profileContextText, value);
    }

    public string LatestDisclaimerText
    {
        get => _latestDisclaimerText;
        set => SetProperty(ref _latestDisclaimerText, value);
    }

    public AsyncRelayCommand ScanCommand { get; }

    public AsyncRelayCommand StopScanCommand { get; }

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public AsyncRelayCommand ClearHistoryCommand { get; }

    public async Task InitializeAsync()
    {
        try
        {
            await _store.InitializeAsync();
            await RefreshProfilesAsync();
            _settings = await _store.LoadSettingsAsync();
            RaiseCommandStates();
        }
        catch (Exception ex)
        {
            StatusText = $"Storage initialization failed: {ex.Message}";
        }
    }

    public async Task RefreshProfilesAsync()
    {
        Profiles.Clear();
        foreach (var profile in await _store.LoadProfilesAsync())
        {
            Profiles.Add(profile);
        }

        SelectedProfile = ActiveProfile is null ? Profiles.FirstOrDefault() : Profiles.FirstOrDefault(p => p.Id == ActiveProfile.Id);
    }

    public async Task<UserProfile> CreateProfileAsync(string displayName, DateOnly birthDate, BiologicalSex biologicalSex)
    {
        var profile = await _store.CreateProfileAsync(displayName, birthDate, biologicalSex);
        Profiles.Add(profile);
        SelectedProfile = profile;
        return profile;
    }

    public async Task UpdateProfileAsync(UserProfile profile, string displayName, DateOnly birthDate, BiologicalSex biologicalSex)
    {
        var updated = await _store.UpdateProfileAsync(profile, displayName, birthDate, biologicalSex);
        ReplaceProfile(profile, updated);

        if (ActiveProfile?.Id == updated.Id)
        {
            ActiveProfile = updated;
            await LoadHistoryAsync();
        }

        SelectedProfile = updated;
    }

    public async Task SetProfilePhotoAsync(UserProfile profile, XamlRoot xamlRoot)
    {
        string? photoPath;
        try
        {
            photoPath = await _profilePhotoService.PickAndCropAsync(_owner, xamlRoot, profile.Id);
        }
        catch (Exception ex)
        {
            StatusText = $"Profile photo failed: {ex.Message}";
            return;
        }

        if (photoPath is null)
        {
            StatusText = "Profile photo selection canceled.";
            return;
        }

        var updated = await _store.UpdateProfilePhotoAsync(profile, photoPath);
        ReplaceProfile(profile, updated);
        if (ActiveProfile?.Id == updated.Id)
        {
            ActiveProfile = updated;
        }

        SelectedProfile = updated;
        StatusText = $"Updated photo for {updated.DisplayName}.";
    }

    public async Task RemoveProfilePhotoAsync(UserProfile profile)
    {
        var updated = await _store.UpdateProfilePhotoAsync(profile, null);
        ReplaceProfile(profile, updated);
        if (ActiveProfile?.Id == updated.Id)
        {
            ActiveProfile = updated;
        }

        SelectedProfile = updated;
        StatusText = $"Removed photo for {updated.DisplayName}.";
    }

    public async Task DeleteProfileAsync(UserProfile profile)
    {
        await _store.DeleteProfileAsync(profile.Id);
        Profiles.Remove(profile);

        if (ActiveProfile?.Id == profile.Id)
        {
            ActiveProfile = null;
            History.Clear();
            WeeklySummaries.Clear();
            ClearLastReading();
        }

        SelectedProfile = Profiles.FirstOrDefault();
        RaiseCommandStates();
    }

    public async Task SetActiveProfileAsync(UserProfile profile)
    {
        ActiveProfile = profile;
        SelectedProfile = profile;
        await LoadHistoryAsync();
    }

    public async Task LoadHistoryAsync()
    {
        History.Clear();
        WeeklySummaries.Clear();
        if (ActiveProfile is null)
        {
            ClearLastReading();
            return;
        }

        foreach (var item in await _store.LoadRecentSessionsAsync(ActiveProfile.Id))
        {
            History.Add(item);
        }

        foreach (var summary in await _store.LoadWeeklySummariesAsync(ActiveProfile.Id))
        {
            WeeklySummaries.Add(summary);
        }

        if (History.Count > 0)
        {
            ShowSession(History[0]);
        }
        else
        {
            ClearLastReading();
        }

        RaiseCommandStates();
    }

    public bool HasRememberedDevice()
    {
        return _settings.HasRememberedDevice;
    }

    public async Task AutoConnectRememberedDeviceAsync()
    {
        if (!_settings.HasRememberedDevice || string.IsNullOrWhiteSpace(_settings.RememberedBluetoothAddress))
        {
            return;
        }

        if (!ulong.TryParse(_settings.RememberedBluetoothAddress, System.Globalization.NumberStyles.HexNumber, null, out var address))
        {
            StatusText = "Remembered device address is invalid. Open Device setup to reconnect.";
            return;
        }

        _autoConnectAddress = address;
        StatusText = $"Trying remembered device {_settings.RememberedDeviceName ?? _settings.RememberedBluetoothAddress}...";

        try
        {
            await ConnectDeviceAsync(new DiscoveredBleDevice(address, _settings.RememberedDeviceName ?? $"BLE {address:X12}", true, 0), rememberDevice: false);
        }
        catch
        {
            StatusText = "Waiting for remembered device to advertise.";
            await ScanAsync();
        }
    }

    public async Task StartScanAsync()
    {
        await ScanAsync();
    }

    public Task StartRememberedDeviceWatchAsync()
    {
        if (!_settings.HasRememberedDevice || string.IsNullOrWhiteSpace(_settings.RememberedBluetoothAddress))
        {
            StatusText = "No remembered QardioArm. Open Device setup to connect one.";
            return Task.CompletedTask;
        }

        if (!ulong.TryParse(_settings.RememberedBluetoothAddress, System.Globalization.NumberStyles.HexNumber, null, out var address))
        {
            StatusText = "Remembered device address is invalid. Open Device setup to reconnect.";
            return Task.CompletedTask;
        }

        _rememberedWatchAddress = address;
        _isRememberedDeviceWatchRunning = true;
        _watchFailureCount = 0;
        _nextWatchConnectAttempt = DateTimeOffset.MinValue;
        IsScanning = false;
        _bleService.StartScanning(active: false);
        StatusText = $"Watching for {_settings.RememberedDeviceName ?? _settings.RememberedBluetoothAddress}.";
        return Task.CompletedTask;
    }

    public Task StopRememberedDeviceWatchAsync()
    {
        if (_isRememberedDeviceWatchRunning)
        {
            _bleService.StopScanning();
        }

        _isRememberedDeviceWatchRunning = false;
        _rememberedWatchAddress = null;
        StatusText = "Qardio watch paused.";
        return Task.CompletedTask;
    }

    public async Task RunGuidedSessionAsync(IProgress<GuidedSessionProgress> progress, CancellationToken cancellationToken)
    {
        if (ActiveProfile is null)
        {
            throw new InvalidOperationException("Select a profile before starting a session.");
        }

        if (!IsConnected)
        {
            throw new InvalidOperationException("Connect the QardioArm before starting a session.");
        }

        IsSessionRunning = true;
        try
        {
            await CountdownAsync(progress, "Get ready", "Rest your arm, breathe deeply. Starting in", 3, cancellationToken);
            var first = await CaptureReadingAsync(1, progress, cancellationToken);

            await RestCountdownAsync(progress, first, cancellationToken);
            await CountdownAsync(progress, "Second reading", "Stay relaxed. Starting in", 3, cancellationToken);
            var second = await CaptureReadingAsync(2, progress, cancellationToken);

            var average = MeasurementSessionCalculator.Average(first.Reading, second.Reading);
            var session = await _store.SaveSessionAsync(
                ActiveProfile.Id,
                first.Reading,
                second.Reading,
                average,
                string.IsNullOrWhiteSpace(_bleService.ConnectedDeviceName) ? "QardioArm" : _bleService.ConnectedDeviceName,
                _bleService.ConnectedBluetoothAddress);

            Enqueue(() =>
            {
                History.Insert(0, session);
                _ = LoadWeeklySummariesForActiveProfileAsync();
                ShowSession(session);
                StatusText = $"Session saved. Samples observed: {first.SampleCount} and {second.SampleCount}.";
                progress.Report(new GuidedSessionProgress("Session complete", $"Average: {session.AverageText} {session.UnitText}", null, "Both readings were saved.", IsComplete: true));
                RaiseCommandStates();
            });
        }
        catch (OperationCanceledException)
        {
            await StopMeasurementBestEffortAsync();
            StatusText = "Guided session canceled.";
            throw;
        }
        finally
        {
            IsSessionRunning = false;
            ClearPendingCapture();
        }
    }

    private async Task ScanAsync()
    {
        try
        {
            Devices.Clear();
            _allDevices.Clear();
            SelectedDevice = null;
            _isRememberedDeviceWatchRunning = false;
            _rememberedWatchAddress = null;
            _bleService.StartScanning(active: true);
            IsScanning = true;
        }
        catch (Exception ex)
        {
            StatusText = $"BLE scan failed: {ex.Message}";
        }

        await Task.CompletedTask;
    }

    private Task StopScanAsync()
    {
        _bleService.StopScanning();
        IsScanning = false;
        StatusText = "Scan stopped.";
        return Task.CompletedTask;
    }

    private async Task ConnectSelectedDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await ConnectDeviceAsync(SelectedDevice.ToDiscoveredDevice(), rememberDevice: true);
    }

    private async Task ConnectDeviceAsync(DiscoveredBleDevice device, bool rememberDevice)
    {
        try
        {
            _bleService.StopScanning();
            IsScanning = false;
            await _bleService.ConnectAsync(device);
            SetConnected(true);

            if (rememberDevice)
            {
                await SaveRememberedDeviceAsync(device.Name, device.BluetoothAddressText);
            }

            RememberedDeviceConnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SetConnected(false);
            StatusText = $"Connect failed: {ex.Message}";
            throw;
        }
    }

    private async Task SaveRememberedDeviceAsync(string name, string address)
    {
        await _store.SaveRememberedDeviceAsync(name, address);
        _settings = new AppSettings(name, address);
        StatusText = $"Remembered {name}.";
    }

    private async Task DisconnectAsync()
    {
        try
        {
            await StopMeasurementBestEffortAsync();
            await _bleService.DisconnectAsync();
            SetConnected(false);
        }
        catch (Exception ex)
        {
            StatusText = $"Disconnect failed: {ex.Message}";
        }
    }

    private async Task StopMeasurementAsync()
    {
        try
        {
            await StopMeasurementBestEffortAsync();
            StatusText = "Measurement stopped.";
        }
        catch (Exception ex)
        {
            StatusText = $"Stop failed: {ex.Message}";
        }
    }

    private async Task StopMeasurementBestEffortAsync()
    {
        ClearPendingCapture();
        if (!IsConnected)
        {
            return;
        }

        try
        {
            await _bleService.StopMeasurementAsync();
        }
        catch
        {
            // Stop is best-effort during cancellation and disconnect.
        }

        IsMeasurementRunning = false;
    }

    private async Task ExportAsync()
    {
        if (ActiveProfile is null)
        {
            return;
        }

        try
        {
            var path = await _csvExportService.ExportAsync(_owner, ActiveProfile, History.ToList());
            StatusText = path is null ? "CSV export canceled." : $"CSV exported to {path}.";
        }
        catch (Exception ex)
        {
            StatusText = $"CSV export failed: {ex.Message}";
        }
    }

    private async Task ClearHistoryAsync()
    {
        if (ActiveProfile is null)
        {
            return;
        }

        try
        {
            await _store.ClearProfileDataAsync(ActiveProfile.Id);
            History.Clear();
            WeeklySummaries.Clear();
            ClearLastReading();
            StatusText = $"Reading history cleared for {ActiveProfile.DisplayName}.";
            RaiseCommandStates();
        }
        catch (Exception ex)
        {
            StatusText = $"Clear history failed: {ex.Message}";
        }
    }

    private void OnDeviceFound(object? sender, BleDeviceFoundEventArgs e)
    {
        Enqueue(() =>
        {
            var existing = _allDevices.FirstOrDefault(d => d.BluetoothAddress == e.Device.BluetoothAddress);
            if (existing is null)
            {
                existing = new BleDeviceViewModel(e.Device);
                _allDevices.Add(existing);
            }
            else
            {
                existing.Update(e.Device);
            }

            RefreshDisplayedDevices();

            if (_autoConnectAddress == e.Device.BluetoothAddress && !IsConnected)
            {
                _ = ConnectDeviceAsync(e.Device, rememberDevice: false);
                _autoConnectAddress = null;
            }

            if (_isRememberedDeviceWatchRunning &&
                _rememberedWatchAddress == e.Device.BluetoothAddress &&
                !IsConnected)
            {
                _ = ConnectRememberedDeviceFromWatchAsync(e.Device);
            }
        });
    }

    private async Task ConnectRememberedDeviceFromWatchAsync(DiscoveredBleDevice device)
    {
        if (_watchConnectInProgress || IsConnected || DateTimeOffset.UtcNow < _nextWatchConnectAttempt)
        {
            return;
        }

        _watchConnectInProgress = true;
        try
        {
            StatusText = $"Remembered QardioArm detected. Connecting to {device.Name}...";
            await ConnectDeviceAsync(device, rememberDevice: false);
            _watchFailureCount = 0;
            _nextWatchConnectAttempt = DateTimeOffset.MinValue;
        }
        catch
        {
            _watchFailureCount++;
            var backoff = _watchFailureCount switch
            {
                1 => TimeSpan.FromSeconds(15),
                2 => TimeSpan.FromSeconds(30),
                _ => TimeSpan.FromSeconds(60)
            };
            _nextWatchConnectAttempt = DateTimeOffset.UtcNow.Add(backoff);
            StatusText = $"Qardio detected, but connect failed. Will retry after {backoff.TotalSeconds:0} seconds.";

            if (_isRememberedDeviceWatchRunning)
            {
                _bleService.StartScanning(active: false);
            }
        }
        finally
        {
            _watchConnectInProgress = false;
        }
    }

    private void OnReadingReceived(object? sender, BloodPressureReading reading)
    {
        Enqueue(() =>
        {
            if (_captureCompletion is null)
            {
                StatusText = "Ignoring measurement outside a guided session.";
                return;
            }

            _pendingReading = reading;
            _pendingSampleCount++;
            StatusText = $"Measurement sample {_pendingSampleCount} received; waiting for final value.";
            ScheduleFinalReadingCommit();
        });
    }

    private async Task CountdownAsync(IProgress<GuidedSessionProgress> progress, string title, string message, int seconds, CancellationToken cancellationToken)
    {
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            progress.Report(new GuidedSessionProgress(title, $"{message} {remaining}.", remaining));
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private async Task RestCountdownAsync(IProgress<GuidedSessionProgress> progress, ReadingCaptureResult first, CancellationToken cancellationToken)
    {
        for (var remaining = 60; remaining > 0; remaining--)
        {
            progress.Report(new GuidedSessionProgress(
                "Rest between readings",
                "First reading complete. Keep your arm supported and breathe slowly.",
                remaining,
                $"First reading: {FormatReading(first.Reading)}"));
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private async Task<ReadingCaptureResult> CaptureReadingAsync(int readingNumber, IProgress<GuidedSessionProgress> progress, CancellationToken cancellationToken)
    {
        ClearPendingCapture();
        _captureCompletion = new TaskCompletionSource<ReadingCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsMeasurementRunning = true;
        progress.Report(new GuidedSessionProgress($"Reading {readingNumber}", "Measuring. Stay still and keep breathing slowly."));

        await _bleService.StartMeasurementAsync();

        using var timeout = new CancellationTokenSource(ReadingTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await using var registration = linked.Token.Register(() =>
        {
            _captureCompletion?.TrySetCanceled(linked.Token);
        });

        try
        {
            var result = await _captureCompletion.Task;
            progress.Report(new GuidedSessionProgress($"Reading {readingNumber} complete", FormatReading(result.Reading), null, $"Samples observed: {result.SampleCount}."));
            return result;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Reading {readingNumber} did not complete within 90 seconds.");
        }
        finally
        {
            IsMeasurementRunning = false;
        }
    }

    private void ScheduleFinalReadingCommit()
    {
        _finalReadingDelay?.Cancel();
        _finalReadingDelay?.Dispose();
        _finalReadingDelay = new CancellationTokenSource();
        var token = _finalReadingDelay.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FinalReadingQuietPeriod, token);
                CompletePendingCapture();
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void CompletePendingCapture()
    {
        var reading = _pendingReading;
        var completion = _captureCompletion;
        if (reading is null || completion is null)
        {
            return;
        }

        var sampleCount = _pendingSampleCount;
        ClearPendingCapture(keepCompletion: true);
        completion.TrySetResult(new ReadingCaptureResult(reading, sampleCount));
    }

    private void ClearPendingCapture(bool keepCompletion = false)
    {
        _finalReadingDelay?.Cancel();
        _finalReadingDelay?.Dispose();
        _finalReadingDelay = null;
        _pendingReading = null;
        _pendingSampleCount = 0;
        if (!keepCompletion)
        {
            _captureCompletion = null;
        }
    }

    private void ShowSession(MeasurementSessionItem item)
    {
        LastSystolicText = FormatNumber(item.Average.Systolic);
        LastDiastolicText = FormatNumber(item.Average.Diastolic);
        LastMeanArterialPressureText = FormatNumber(item.Average.MeanArterialPressure);
        LastPulseText = item.Average.PulseRate is null ? "--" : FormatNumber(item.Average.PulseRate.Value);
        LastMeasuredAtText = item.CompletedAtText;
        LastStatusText = $"Average of two readings: {item.FirstReadingText} and {item.SecondReadingText}. {item.StatusText}";

        if (ActiveProfile is not null)
        {
            var category = BloodPressureClassifier.Classify(item.Average, ActiveProfile);
            LatestCategoryText = category.DisplayLabel;
            LatestTargetText = category.TargetText;
            LatestDisclaimerText = category.DisclaimerText;
        }
    }

    private void ClearLastReading()
    {
        LastSystolicText = "--";
        LastDiastolicText = "--";
        LastMeanArterialPressureText = "--";
        LastPulseText = "--";
        LastMeasuredAtText = "--";
        LastStatusText = "--";
        LatestCategoryText = "No readings";
        LatestTargetText = "AHA adult reference: below 120/80";
        LatestDisclaimerText = "LibreArm is not medical advice.";
    }

    private async Task LoadWeeklySummariesForActiveProfileAsync()
    {
        if (ActiveProfile is null)
        {
            return;
        }

        var summaries = await _store.LoadWeeklySummariesAsync(ActiveProfile.Id);
        Enqueue(() =>
        {
            WeeklySummaries.Clear();
            foreach (var summary in summaries)
            {
                WeeklySummaries.Add(summary);
            }
        });
    }

    private void SetConnected(bool connected)
    {
        IsConnected = connected;
        if (!connected)
        {
            IsMeasurementRunning = false;
            ConnectionText = "Disconnected";
            BatteryText = "Battery: not available";
            ClearPendingCapture();
            if (_isRememberedDeviceWatchRunning && !_watchConnectInProgress)
            {
                _bleService.StartScanning(active: false);
            }
        }
        else
        {
            ConnectionText = "Connected";
        }
    }

    private void RefreshDisplayedDevices()
    {
        var selectedAddress = SelectedDevice?.BluetoothAddress;
        var ordered = _allDevices
            .Where(d => ShowAllDevices || d.IsPrioritized)
            .OrderByDescending(d => d.Priority)
            .ThenBy(d => d.Name)
            .ToList();

        for (var i = Devices.Count - 1; i >= 0; i--)
        {
            if (!ordered.Contains(Devices[i]))
            {
                Devices.RemoveAt(i);
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var currentIndex = Devices.IndexOf(ordered[i]);
            if (currentIndex < 0)
            {
                Devices.Insert(i, ordered[i]);
            }
            else if (currentIndex != i)
            {
                Devices.Move(currentIndex, i);
            }
        }

        SelectedDevice = selectedAddress is null ? null : Devices.FirstOrDefault(d => d.BluetoothAddress == selectedAddress);
    }

    private void Enqueue(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcher.TryEnqueue(() => action());
    }

    private void RaiseCommandStates()
    {
        ScanCommand.RaiseCanExecuteChanged();
        StopScanCommand.RaiseCanExecuteChanged();
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        ClearHistoryCommand.RaiseCanExecuteChanged();
    }

    private static string FormatReading(BloodPressureReading reading)
    {
        return $"{FormatNumber(reading.Systolic)}/{FormatNumber(reading.Diastolic)} {reading.UnitLabel}";
    }

    private static string FormatNumber(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? value.ToString() : value.ToString("0.#");
    }

    private void ReplaceProfile(UserProfile oldProfile, UserProfile updated)
    {
        var index = Profiles.IndexOf(oldProfile);
        if (index < 0)
        {
            index = Profiles.ToList().FindIndex(profile => profile.Id == updated.Id);
        }

        if (index >= 0)
        {
            Profiles[index] = updated;
        }
    }
}
