namespace LibreArm_Desktop.Models;

using LibreArm.Core.Models;

public sealed record ReadingHistoryItem(
    long Id,
    BloodPressureReading Reading,
    string DeviceName,
    string BluetoothAddress)
{
    public string MeasuredAtText => (Reading.Timestamp ?? Reading.ReceivedAt.LocalDateTime).ToString("yyyy-MM-dd HH:mm:ss");

    public string SystolicText => FormatNumber(Reading.Systolic);

    public string DiastolicText => FormatNumber(Reading.Diastolic);

    public string MeanArterialPressureText => FormatNumber(Reading.MeanArterialPressure);

    public string PulseText => Reading.PulseRate is null ? "" : FormatNumber(Reading.PulseRate.Value);

    public string UnitText => Reading.UnitLabel;

    public string StatusText => Reading.MeasurementStatusText;

    private static string FormatNumber(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? value.ToString() : value.ToString("0.#");
    }
}
