namespace LibreArm_Desktop.Services;

using System.Text;
using LibreArm.Core.Models;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;

public sealed class CsvExportService
{
    public async Task<string?> ExportAsync(Window owner, UserProfile profile, IReadOnlyList<MeasurementSessionItem> sessions)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"librearm-{profile.DisplayName}-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        CachedFileManager.DeferUpdates(file);
        await FileIO.WriteTextAsync(file, BuildCsv(profile, sessions));
        await CachedFileManager.CompleteUpdatesAsync(file);
        return file.Path;
    }

    private static string BuildCsv(UserProfile profile, IEnumerable<MeasurementSessionItem> sessions)
    {
        var sessionList = sessions.OrderBy(s => s.CompletedAt).ToList();
        var builder = new StringBuilder();
        builder.AppendLine("Profile,SessionCompletedAt,RowType,ReadingNumber,Systolic,Diastolic,MeanArterialPressure,Unit,Pulse,BpCategory,Status,DeviceName,BluetoothAddress,ReceivedAt,RawPayload");
        foreach (var item in sessionList)
        {
            AppendRow(builder, profile, item, "Average", null, item.Average);
            AppendRow(builder, profile, item, "Reading", 1, item.FirstReading);
            AppendRow(builder, profile, item, "Reading", 2, item.SecondReading);
        }

        builder.AppendLine();
        builder.AppendLine("Profile,WeekStart,WeekEnd,SessionCount,AverageSystolic,AverageDiastolic,AverageMeanArterialPressure,AveragePulse,BpCategory");
        foreach (var weekly in BuildWeeklySummaries(sessionList))
        {
            AppendCsv(builder, profile.DisplayName);
            AppendCsv(builder, weekly.WeekStart.ToString("yyyy-MM-dd"));
            AppendCsv(builder, weekly.WeekEnd.ToString("yyyy-MM-dd"));
            AppendCsv(builder, weekly.SessionCount.ToString());
            AppendCsv(builder, weekly.AverageSystolic is null ? "" : FormatNumber(weekly.AverageSystolic.Value));
            AppendCsv(builder, weekly.AverageDiastolic is null ? "" : FormatNumber(weekly.AverageDiastolic.Value));
            AppendCsv(builder, weekly.AverageMeanArterialPressure is null ? "" : FormatNumber(weekly.AverageMeanArterialPressure.Value));
            AppendCsv(builder, weekly.AveragePulseRate is null ? "" : FormatNumber(weekly.AveragePulseRate.Value));
            AppendCsv(builder, weekly.CategoryText, endOfLine: true);
        }

        return builder.ToString();
    }

    private static void AppendRow(
        StringBuilder builder,
        UserProfile profile,
        MeasurementSessionItem session,
        string rowType,
        int? readingNumber,
        LibreArm.Core.Models.BloodPressureReading reading)
    {
        AppendCsv(builder, profile.DisplayName);
        AppendCsv(builder, session.CompletedAt.ToString("O"));
        AppendCsv(builder, rowType);
        AppendCsv(builder, readingNumber?.ToString() ?? "");
        AppendCsv(builder, FormatNumber(reading.Systolic));
        AppendCsv(builder, FormatNumber(reading.Diastolic));
        AppendCsv(builder, FormatNumber(reading.MeanArterialPressure));
        AppendCsv(builder, reading.UnitLabel);
        AppendCsv(builder, reading.PulseRate is null ? "" : FormatNumber(reading.PulseRate.Value));
        AppendCsv(builder, BloodPressureClassifier.Classify(reading, profile).DisplayLabel);
        AppendCsv(builder, reading.MeasurementStatusText);
        AppendCsv(builder, session.DeviceName);
        AppendCsv(builder, session.BluetoothAddress);
        AppendCsv(builder, reading.ReceivedAt.ToString("O"));
        AppendCsv(builder, Convert.ToHexString(reading.RawPayload), endOfLine: true);
    }

    private static string FormatNumber(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? value.ToString() : value.ToString("0.#");
    }

    private static IReadOnlyList<WeeklyBloodPressureSummary> BuildWeeklySummaries(IReadOnlyList<MeasurementSessionItem> sessions)
    {
        var currentWeekStart = GetWeekStart(DateOnly.FromDateTime(DateTime.Today));
        var weeks = Enumerable.Range(0, 12)
            .Select(offset => currentWeekStart.AddDays(-7 * (11 - offset)))
            .ToList();
        var groups = weeks.ToDictionary(week => week, _ => new List<MeasurementSessionItem>());
        foreach (var session in sessions)
        {
            var week = GetWeekStart(DateOnly.FromDateTime(session.CompletedAt.LocalDateTime));
            if (groups.TryGetValue(week, out var group))
            {
                group.Add(session);
            }
        }

        return weeks.Select(week =>
        {
            var group = groups[week];
            return new WeeklyBloodPressureSummary(
                week,
                week.AddDays(6),
                group.Count,
                group.Count == 0 ? null : group.Average(s => s.Average.Systolic),
                group.Count == 0 ? null : group.Average(s => s.Average.Diastolic),
                group.Count == 0 ? null : group.Average(s => s.Average.MeanArterialPressure),
                AverageNullable(group.Select(s => s.Average.PulseRate)));
        }).ToList();
    }

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static double? AverageNullable(IEnumerable<double?> values)
    {
        var concrete = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return concrete.Count == 0 ? null : concrete.Average();
    }

    private static void AppendCsv(StringBuilder builder, string value, bool endOfLine = false)
    {
        var escaped = value.Replace("\"", "\"\"");
        builder.Append('"').Append(escaped).Append('"');
        builder.Append(endOfLine ? Environment.NewLine : ',');
    }
}
