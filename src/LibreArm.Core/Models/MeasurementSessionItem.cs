namespace LibreArm.Core.Models;

public sealed record MeasurementSessionItem(
    long Id,
    long ProfileId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    BloodPressureReading Average,
    BloodPressureReading FirstReading,
    BloodPressureReading SecondReading,
    string DeviceName,
    string BluetoothAddress)
{
    public string CompletedAtText => CompletedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    public string AverageText => $"{FormatNumber(Average.Systolic)}/{FormatNumber(Average.Diastolic)}";

    public string MeanArterialPressureText => FormatNumber(Average.MeanArterialPressure);

    public string PulseText => Average.PulseRate is null ? "" : FormatNumber(Average.PulseRate.Value);

    public string UnitText => Average.UnitLabel;

    public string StatusText => Average.MeasurementStatusText;

    public string FirstReadingText => $"{FormatNumber(FirstReading.Systolic)}/{FormatNumber(FirstReading.Diastolic)}";

    public string SecondReadingText => $"{FormatNumber(SecondReading.Systolic)}/{FormatNumber(SecondReading.Diastolic)}";

    private static string FormatNumber(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? value.ToString() : value.ToString("0.#");
    }
}
