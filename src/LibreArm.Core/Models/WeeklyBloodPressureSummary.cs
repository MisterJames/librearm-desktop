namespace LibreArm.Core.Models;

public sealed record WeeklyBloodPressureSummary(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int SessionCount,
    double? AverageSystolic,
    double? AverageDiastolic,
    double? AverageMeanArterialPressure,
    double? AveragePulseRate)
{
    public bool HasReadings => SessionCount > 0;

    public string WeekLabel => $"{WeekStart:MMM d} - {WeekEnd:MMM d}";

    public BloodPressureCategoryResult? Category => HasReadings && AverageSystolic is not null && AverageDiastolic is not null
        ? BloodPressureClassifier.ClassifyAdult(AverageSystolic.Value, AverageDiastolic.Value)
        : null;

    public double SystolicChartValue => AverageSystolic ?? 0;

    public double DiastolicChartValue => AverageDiastolic ?? 0;

    public string SystolicText => AverageSystolic is null ? "--" : AverageSystolic.Value.ToString("0.#");

    public string DiastolicText => AverageDiastolic is null ? "--" : AverageDiastolic.Value.ToString("0.#");

    public string SessionCountText => SessionCount == 1 ? "1 session" : $"{SessionCount} sessions";

    public string CategoryText => Category?.DisplayLabel ?? "No readings";
}
