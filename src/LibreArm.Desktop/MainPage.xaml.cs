using LibreArm.Core.Models;
using LibreArm_Desktop.Models;
using LibreArm_Desktop.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using System.Collections.Specialized;

namespace LibreArm_Desktop;

public sealed partial class MainPage : Page
{
    private bool _initialized;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public MainViewModel? ViewModel { get; private set; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        ViewModel = new MainViewModel(DispatcherQueue, App.MainWindow ?? throw new InvalidOperationException("Main window is not available."));
        ViewModel.RememberedDeviceConnected += (_, _) => NavigateTo("readings");
        ViewModel.WeeklySummaries.CollectionChanged += OnWeeklySummariesChanged;
        DataContext = ViewModel;
        App.MainWindow?.AttachPage(this, ViewModel);

        await ViewModel.InitializeAsync();
        await CompleteStartupFlowAsync();
    }

    private async Task CompleteStartupFlowAsync()
    {
        if (ViewModel is null)
        {
            return;
        }

        var hadNoProfiles = ViewModel.Profiles.Count == 0;
        UserProfile? profile;
        if (hadNoProfiles)
        {
            profile = await PromptCreateProfileAsync(required: true);
        }
        else
        {
            profile = await PromptSelectProfileAsync();
        }

        if (profile is null)
        {
            return;
        }

        await ViewModel.SetActiveProfileAsync(profile);

        if (ViewModel.HasRememberedDevice())
        {
            NavigateTo("readings");
            _ = ViewModel.AutoConnectRememberedDeviceAsync();
            return;
        }

        NavigateTo("device");
        await ViewModel.StartScanAsync();
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        NavigateTo(item.Tag?.ToString() ?? "readings");
    }

    private async void OnStartSessionClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        await ShowGuidedSessionDialogAsync();
    }

    private void OnHideToTrayClick(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.StartTrayWatchAndHide();
    }

    private async void OnSwitchProfileClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProfile is null)
        {
            await ShowMessageAsync("Select a profile", "Choose a profile from the list first.");
            return;
        }

        await ViewModel.SetActiveProfileAsync(ViewModel.SelectedProfile);
        NavigateTo("readings");
        _ = ViewModel.AutoConnectRememberedDeviceAsync();
    }

    private async void OnAddProfileClick(object sender, RoutedEventArgs e)
    {
        var profile = await PromptCreateProfileAsync(required: false);
        if (profile is null || ViewModel is null)
        {
            return;
        }

        await ViewModel.SetActiveProfileAsync(profile);
        NavigateTo(ViewModel.HasRememberedDevice() ? "readings" : "device");
        if (ViewModel.HasRememberedDevice())
        {
            _ = ViewModel.AutoConnectRememberedDeviceAsync();
        }
        else
        {
            await ViewModel.StartScanAsync();
        }
    }

    private async void OnRenameProfileClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProfile is null)
        {
            await ShowMessageAsync("Select a profile", "Choose a profile from the list first.");
            return;
        }

        var details = await PromptProfileDetailsAsync("Edit profile", ViewModel.SelectedProfile, required: false);
        if (details is null)
        {
            return;
        }

        await ViewModel.UpdateProfileAsync(ViewModel.SelectedProfile, details.Value.Name, details.Value.BirthDate, details.Value.BiologicalSex);
    }

    private async void OnSetProfilePhotoClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProfile is null)
        {
            await ShowMessageAsync("Select a profile", "Choose a profile from the list first.");
            return;
        }

        await ViewModel.SetProfilePhotoAsync(ViewModel.SelectedProfile, XamlRoot);
    }

    private async void OnRemoveProfilePhotoClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProfile is null)
        {
            await ShowMessageAsync("Select a profile", "Choose a profile from the list first.");
            return;
        }

        await ViewModel.RemoveProfilePhotoAsync(ViewModel.SelectedProfile);
    }

    private async void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProfile is null)
        {
            await ShowMessageAsync("Select a profile", "Choose a profile from the list first.");
            return;
        }

        var profile = ViewModel.SelectedProfile;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete {profile.DisplayName}?",
            Content = "This removes that profile and its local sessions. The remembered device is shared and will stay available.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.DeleteProfileAsync(profile);
        if (ViewModel.Profiles.Count == 0)
        {
            var replacement = await PromptCreateProfileAsync(required: true);
            if (replacement is not null)
            {
                await ViewModel.SetActiveProfileAsync(replacement);
            }
        }
        else if (ViewModel.ActiveProfile is null && ViewModel.SelectedProfile is not null)
        {
            await ViewModel.SetActiveProfileAsync(ViewModel.SelectedProfile);
        }
    }

    private async void OnQuickProfileClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var profile = await ShowProfileSwitcherDialogAsync("Switch profile", required: false);
        if (profile is not null)
        {
            await ViewModel.SetActiveProfileAsync(profile);
            NavigateTo("readings");
            _ = ViewModel.AutoConnectRememberedDeviceAsync();
        }
    }

    private async Task ShowGuidedSessionDialogAsync()
    {
        if (ViewModel is null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        var title = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var message = new TextBlock { TextWrapping = TextWrapping.WrapWholeWords };
        var countdown = new TextBlock { FontSize = 48, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var detail = new TextBlock { TextWrapping = TextWrapping.WrapWholeWords };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(title);
        panel.Children.Add(message);
        panel.Children.Add(countdown);
        panel.Children.Add(detail);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Guided blood pressure session",
            Content = panel,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        var complete = false;
        var progress = new Progress<GuidedSessionProgress>(state =>
        {
            title.Text = state.Title;
            message.Text = state.Message;
            countdown.Text = state.CountdownSeconds?.ToString() ?? "";
            detail.Text = state.Detail ?? "";
            if (state.IsComplete)
            {
                complete = true;
                dialog.CloseButtonText = "Close";
            }
        });

        dialog.CloseButtonClick += (_, _) =>
        {
            if (!complete)
            {
                cancellation.Cancel();
            }
        };

        var runner = RunGuidedSessionForDialogAsync(progress, cancellation.Token, () =>
        {
            complete = true;
            dialog.CloseButtonText = "Close";
        });

        await dialog.ShowAsync();
        if (!complete)
        {
            cancellation.Cancel();
        }

        await runner;
    }

    private async Task RunGuidedSessionForDialogAsync(IProgress<GuidedSessionProgress> progress, CancellationToken cancellationToken, Action markComplete)
    {
        if (ViewModel is null)
        {
            return;
        }

        try
        {
            await ViewModel.RunGuidedSessionAsync(progress, cancellationToken);
            markComplete();
        }
        catch (OperationCanceledException)
        {
            progress.Report(new GuidedSessionProgress("Session canceled", "No session was saved.", null, "You can start again when ready.", IsComplete: true));
            markComplete();
        }
        catch (Exception ex)
        {
            progress.Report(new GuidedSessionProgress("Session stopped", ex.Message, null, "Check connection and try again.", IsComplete: true));
            markComplete();
        }
    }

    private async Task<UserProfile?> PromptSelectProfileAsync()
    {
        return await ShowProfileSwitcherDialogAsync("Select profile", required: true);
    }

    private async Task<UserProfile?> ShowProfileSwitcherDialogAsync(string title, bool required)
    {
        if (ViewModel is null)
        {
            return null;
        }

        while (true)
        {
            if (ViewModel.Profiles.Count == 0)
            {
                return await PromptCreateProfileAsync(required);
            }

            var picker = new ListView
            {
                ItemsSource = ViewModel.Profiles,
                SelectedItem = ViewModel.ActiveProfile ?? ViewModel.SelectedProfile ?? ViewModel.Profiles.FirstOrDefault(),
                SelectionMode = ListViewSelectionMode.Single,
                MinWidth = 340,
                MaxHeight = 420,
                ItemTemplate = (DataTemplate)Resources["QuickProfileTemplate"]
            };
            var createRequested = false;
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = "Choose who is taking readings today." });
            content.Children.Add(picker);
            var createButton = new Button
            {
                Content = "Create profile",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            content.Children.Add(createButton);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = content,
                PrimaryButtonText = "Continue",
                SecondaryButtonText = "Manage profiles",
                DefaultButton = ContentDialogButton.Primary
            };
            if (!required)
            {
                dialog.CloseButtonText = "Cancel";
            }

            createButton.Click += (_, _) =>
            {
                createRequested = true;
                dialog.Hide();
            };

            var result = await dialog.ShowAsync();
            if (createRequested)
            {
                var created = await PromptCreateProfileAsync(required: false);
                if (created is not null)
                {
                    return created;
                }
            }
            else if (result == ContentDialogResult.Primary && picker.SelectedItem is UserProfile profile)
            {
                return profile;
            }
            else if (result == ContentDialogResult.Secondary)
            {
                NavigateTo("profiles");
                return null;
            }
            else if (!required)
            {
                return null;
            }
        }
    }

    private async Task<UserProfile?> PromptCreateProfileAsync(bool required)
    {
        if (ViewModel is null)
        {
            return null;
        }

        while (true)
        {
            var details = await PromptProfileDetailsAsync("Create profile", null, required);
            if (details is null)
            {
                return null;
            }

            try
            {
                return await ViewModel.CreateProfileAsync(details.Value.Name, details.Value.BirthDate, details.Value.BiologicalSex);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Profile error", ex.Message);
            }
        }
    }

    private async Task<ProfileDetails?> PromptProfileDetailsAsync(string title, UserProfile? profile, bool required)
    {
        while (true)
        {
            var nameBox = new TextBox
            {
                Header = "Name",
                PlaceholderText = "Profile name",
                Text = profile?.DisplayName ?? "",
                MinWidth = 280
            };
            var birthDatePicker = new DatePicker
            {
                Header = "Birthdate",
                Date = profile is null
                    ? new DateTimeOffset(new DateTime(1980, 1, 1))
                    : new DateTimeOffset(profile.BirthDate.ToDateTime(TimeOnly.MinValue))
            };
            var sexPicker = new ComboBox
            {
                Header = "Biological sex",
                MinWidth = 280,
                ItemsSource = new[] { BiologicalSex.Unspecified, BiologicalSex.Female, BiologicalSex.Male },
                SelectedItem = profile?.BiologicalSex ?? BiologicalSex.Unspecified
            };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(nameBox);
            panel.Children.Add(birthDatePicker);
            panel.Children.Add(sexPicker);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = panel,
                PrimaryButtonText = "Save",
                DefaultButton = ContentDialogButton.Primary
            };
            if (!required)
            {
                dialog.CloseButtonText = "Cancel";
            }

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(nameBox.Text))
            {
                return new ProfileDetails(
                    nameBox.Text.Trim(),
                    DateOnly.FromDateTime(birthDatePicker.Date.DateTime),
                    sexPicker.SelectedItem is BiologicalSex sex ? sex : BiologicalSex.Unspecified);
            }

            await ShowMessageAsync("Name required", "Enter a profile name.");
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }

    public async Task ShowDeviceSetupAsync()
    {
        NavigateTo("device");
        if (ViewModel is not null)
        {
            await ViewModel.StartScanAsync();
        }
    }

    public void ShowProfiles()
    {
        NavigateTo("profiles");
    }

    public void ShowReadings()
    {
        NavigateTo("readings");
    }

    private void NavigateTo(string tag)
    {
        var normalizedTag = tag == "profiles" || tag == "device" || tag == "history" ? tag : "readings";
        ReadingsScreen.Visibility = normalizedTag == "readings" ? Visibility.Visible : Visibility.Collapsed;
        HistoryScreen.Visibility = normalizedTag == "history" ? Visibility.Visible : Visibility.Collapsed;
        DeviceScreen.Visibility = normalizedTag == "device" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesScreen.Visibility = normalizedTag == "profiles" ? Visibility.Visible : Visibility.Collapsed;

        RootNavigation.SelectedItem = normalizedTag switch
        {
            "history" => HistoryItem,
            "device" => DeviceItem,
            "profiles" => null,
            _ => ReadingsItem
        };
    }

    private async void OnMetricInfoTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "";
        var (title, message) = tag switch
        {
            "systolic" => ("Systolic", "Systolic is the pressure when the heart contracts. Adult BP categories are driven heavily by this top number."),
            "diastolic" => ("Diastolic", "Diastolic is the pressure while the heart rests between beats. It is the bottom number in a blood pressure reading."),
            "map" => ("Mean arterial pressure", "MAP is a calculated estimate of average arterial pressure across a heartbeat. LibreArm shows it as context, but the adult BP status label is based on systolic and diastolic values."),
            _ => ("Reading", "LibreArm shows locally captured blood pressure values for your own records.")
        };

        await ShowMessageAsync(title, message);
        e.Handled = true;
    }

    private void OnWeeklySummariesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DrawTrendCharts();
    }

    private void OnTrendCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawTrendCharts();
    }

    private void DrawTrendCharts()
    {
        if (ViewModel is null || BloodPressureTrendCanvas.ActualWidth <= 0 || PulseTrendCanvas.ActualWidth <= 0)
        {
            return;
        }

        var summaries = ViewModel.WeeklySummaries.ToList();
        DrawBloodPressureChart(summaries);
        DrawPulseChart(summaries);
    }

    private void DrawBloodPressureChart(IReadOnlyList<WeeklyBloodPressureSummary> summaries)
    {
        BloodPressureTrendCanvas.Children.Clear();
        var values = summaries
            .Where(s => s.HasReadings)
            .SelectMany(s => new[] { s.AverageSystolic, s.AverageDiastolic, s.AverageMeanArterialPressure })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (values.Count == 0)
        {
            DrawEmptyChart(BloodPressureTrendCanvas, "No weekly blood pressure averages yet.");
            return;
        }

        var min = Math.Max(40, Math.Floor((Math.Min(values.Min(), 80) - 10) / 10) * 10);
        var max = Math.Ceiling((Math.Max(values.Max(), 140) + 10) / 10) * 10;
        DrawChartFrame(BloodPressureTrendCanvas, min, max, summaries);
        DrawReferenceLine(BloodPressureTrendCanvas, 120, min, max, "120");
        DrawReferenceLine(BloodPressureTrendCanvas, 80, min, max, "80");
        DrawSeries(BloodPressureTrendCanvas, summaries, s => s.AverageSystolic, min, max, Colors.DeepSkyBlue, "Systolic");
        DrawSeries(BloodPressureTrendCanvas, summaries, s => s.AverageDiastolic, min, max, Colors.Coral, "Diastolic");
        DrawSeries(BloodPressureTrendCanvas, summaries, s => s.AverageMeanArterialPressure, min, max, Colors.MediumOrchid, "MAP");
    }

    private void DrawPulseChart(IReadOnlyList<WeeklyBloodPressureSummary> summaries)
    {
        PulseTrendCanvas.Children.Clear();
        var values = summaries
            .Where(s => s.AveragePulseRate.HasValue)
            .Select(s => s.AveragePulseRate!.Value)
            .ToList();

        if (values.Count == 0)
        {
            DrawEmptyChart(PulseTrendCanvas, "No weekly pulse averages yet.");
            return;
        }

        var min = Math.Max(30, Math.Floor((values.Min() - 10) / 10) * 10);
        var max = Math.Ceiling((Math.Max(values.Max(), 100) + 10) / 10) * 10;
        DrawChartFrame(PulseTrendCanvas, min, max, summaries);
        DrawSeries(PulseTrendCanvas, summaries, s => s.AveragePulseRate, min, max, Colors.LightGreen, "Pulse");
    }

    private static void DrawChartFrame(Canvas canvas, double min, double max, IReadOnlyList<WeeklyBloodPressureSummary> summaries)
    {
        var bounds = GetPlotBounds(canvas);
        var axisBrush = new SolidColorBrush(Colors.DimGray);
        var textBrush = new SolidColorBrush(Colors.LightGray);

        for (var i = 0; i <= 3; i++)
        {
            var y = bounds.Top + (bounds.Height * i / 3);
            canvas.Children.Add(new Line
            {
                X1 = bounds.Left,
                X2 = bounds.Right,
                Y1 = y,
                Y2 = y,
                Stroke = axisBrush,
                StrokeThickness = 1,
                Opacity = 0.55
            });

            var label = new TextBlock
            {
                Text = (max - ((max - min) * i / 3)).ToString("0"),
                Foreground = textBrush,
                FontSize = 11
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
            canvas.Children.Add(label);
        }

        if (summaries.Count > 0)
        {
            AddAxisLabel(canvas, summaries[0].WeekStart.ToString("MMM d"), bounds.Left, bounds.Bottom + 4);
            AddAxisLabel(canvas, summaries[^1].WeekStart.ToString("MMM d"), bounds.Right - 44, bounds.Bottom + 4);
        }
    }

    private static void DrawReferenceLine(Canvas canvas, double value, double min, double max, string label)
    {
        if (value < min || value > max)
        {
            return;
        }

        var bounds = GetPlotBounds(canvas);
        var y = ScaleY(value, min, max, bounds);
        var brush = new SolidColorBrush(Colors.Gray);
        canvas.Children.Add(new Line
        {
            X1 = bounds.Left,
            X2 = bounds.Right,
            Y1 = y,
            Y2 = y,
            Stroke = brush,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            StrokeThickness = 1
        });
        AddAxisLabel(canvas, label, bounds.Right + 4, y - 8);
    }

    private static void DrawSeries(
        Canvas canvas,
        IReadOnlyList<WeeklyBloodPressureSummary> summaries,
        Func<WeeklyBloodPressureSummary, double?> selector,
        double min,
        double max,
        Windows.UI.Color color,
        string label)
    {
        var bounds = GetPlotBounds(canvas);
        var points = new PointCollection();
        var brush = new SolidColorBrush(color);

        for (var i = 0; i < summaries.Count; i++)
        {
            var value = selector(summaries[i]);
            if (!value.HasValue)
            {
                continue;
            }

            var point = new Point(ScaleX(i, summaries.Count, bounds), ScaleY(value.Value, min, max, bounds));
            points.Add(point);
            var marker = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = brush
            };
            ToolTipService.SetToolTip(marker, $"{summaries[i].WeekLabel}: {label} {value.Value:0.#}");
            Canvas.SetLeft(marker, point.X - 3.5);
            Canvas.SetTop(marker, point.Y - 3.5);
            canvas.Children.Add(marker);
        }

        if (points.Count < 2)
        {
            return;
        }

        canvas.Children.Insert(0, new Polyline
        {
            Points = points,
            Stroke = brush,
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round
        });
    }

    private static void DrawEmptyChart(Canvas canvas, string message)
    {
        canvas.Children.Clear();
        var label = new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Colors.LightGray),
            FontSize = 14
        };
        Canvas.SetLeft(label, 24);
        Canvas.SetTop(label, Math.Max(16, canvas.ActualHeight / 2 - 12));
        canvas.Children.Add(label);
    }

    private static Rect GetPlotBounds(Canvas canvas)
    {
        var width = Math.Max(120, canvas.ActualWidth);
        var height = Math.Max(80, canvas.ActualHeight);
        return new Rect(36, 12, Math.Max(40, width - 62), Math.Max(30, height - 42));
    }

    private static double ScaleX(int index, int count, Rect bounds)
    {
        if (count <= 1)
        {
            return bounds.Left + bounds.Width / 2;
        }

        return bounds.Left + (bounds.Width * index / (count - 1));
    }

    private static double ScaleY(double value, double min, double max, Rect bounds)
    {
        return bounds.Bottom - ((value - min) / (max - min) * bounds.Height);
    }

    private static void AddAxisLabel(Canvas canvas, string text, double left, double top)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Colors.LightGray),
            FontSize = 11
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        canvas.Children.Add(label);
    }

    private readonly record struct ProfileDetails(string Name, DateOnly BirthDate, BiologicalSex BiologicalSex);
}
