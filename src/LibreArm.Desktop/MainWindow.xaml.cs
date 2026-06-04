using Microsoft.UI.Xaml;
using LibreArm_Desktop.Services;
using LibreArm_Desktop.ViewModels;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace LibreArm_Desktop;

public sealed partial class MainWindow : Window
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private readonly nint _windowHandle;
    private TrayIconService? _trayIcon;
    private MainPage? _mainPage;
    private MainViewModel? _viewModel;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Closing += OnAppWindowClosing;
        _windowHandle = WindowNative.GetWindowHandle(this);
        RootFrame.Navigate(typeof(MainPage));
    }

    public void AttachPage(MainPage page, MainViewModel viewModel)
    {
        _mainPage = page;
        _viewModel = viewModel;
        _viewModel.RememberedDeviceConnected += (_, _) => ShowFromTray();
    }

    public void StartTrayWatchAndHide()
    {
        EnsureTrayIcon();
        if (_viewModel?.HasRememberedDevice() == true)
        {
            _ = _viewModel.StartRememberedDeviceWatchAsync();
            if (_trayIcon is not null)
            {
                _trayIcon.WatchPaused = false;
            }
        }

        HideToTray();
    }

    public void ShowFromTray()
    {
        ShowWindow(_windowHandle, SwShow);
        _ = SetForegroundWindow(_windowHandle);
        Activate();
        _mainPage?.ShowReadings();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        _trayIcon = new TrayIconService(DispatcherQueue);
        _trayIcon.OpenRequested += (_, _) => ShowFromTray();
        _trayIcon.ToggleWatchRequested += async (_, _) => await ToggleWatchAsync();
        _trayIcon.DeviceSetupRequested += async (_, _) => await ShowDeviceSetupFromTrayAsync();
        _trayIcon.ProfilesRequested += (_, _) => ShowProfilesFromTray();
        _trayIcon.ExitRequested += async (_, _) => await ExitFromTrayAsync();
    }

    private void HideToTray()
    {
        EnsureTrayIcon();
        ShowWindow(_windowHandle, SwHide);
    }

    private async Task ToggleWatchAsync()
    {
        if (_viewModel is null || _trayIcon is null)
        {
            return;
        }

        if (_trayIcon.WatchPaused || !_viewModel.IsRememberedDeviceWatchRunning)
        {
            await _viewModel.StartRememberedDeviceWatchAsync();
            _trayIcon.WatchPaused = false;
        }
        else
        {
            await _viewModel.StopRememberedDeviceWatchAsync();
            _trayIcon.WatchPaused = true;
        }
    }

    private async Task ShowDeviceSetupFromTrayAsync()
    {
        ShowWindow(_windowHandle, SwShow);
        Activate();
        if (_mainPage is not null)
        {
            await _mainPage.ShowDeviceSetupAsync();
        }
    }

    private void ShowProfilesFromTray()
    {
        ShowWindow(_windowHandle, SwShow);
        Activate();
        _mainPage?.ShowProfiles();
    }

    private async Task ExitFromTrayAsync()
    {
        _exitRequested = true;
        if (_viewModel is not null)
        {
            await _viewModel.StopRememberedDeviceWatchAsync();
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
        Close();
    }

    private void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
}
