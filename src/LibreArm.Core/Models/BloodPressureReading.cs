namespace LibreArm.Core.Models;

public sealed record BloodPressureReading(
    double Systolic,
    double Diastolic,
    double MeanArterialPressure,
    BloodPressureUnit Unit,
    double? PulseRate,
    DateTime? Timestamp,
    byte? UserId,
    ushort? MeasurementStatus,
    DateTimeOffset ReceivedAt,
    byte[] RawPayload)
{
    public string UnitLabel => Unit == BloodPressureUnit.MillimetersOfMercury ? "mmHg" : "kPa";

    public string MeasurementStatusText => MeasurementStatusFormatter.Format(MeasurementStatus);
}
