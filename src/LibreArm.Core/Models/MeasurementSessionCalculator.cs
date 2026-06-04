namespace LibreArm.Core.Models;

public static class MeasurementSessionCalculator
{
    public static BloodPressureReading Average(BloodPressureReading first, BloodPressureReading second, DateTimeOffset? receivedAt = null)
    {
        var pulseRate = first.PulseRate is null && second.PulseRate is null
            ? null
            : AverageNullable(first.PulseRate, second.PulseRate);

        return new BloodPressureReading(
            AverageValue(first.Systolic, second.Systolic),
            AverageValue(first.Diastolic, second.Diastolic),
            AverageValue(first.MeanArterialPressure, second.MeanArterialPressure),
            first.Unit,
            pulseRate,
            DateTime.Now,
            null,
            CombineStatus(first.MeasurementStatus, second.MeasurementStatus),
            receivedAt ?? DateTimeOffset.Now,
            []);
    }

    private static double AverageValue(double first, double second)
    {
        return (first + second) / 2d;
    }

    private static double? AverageNullable(double? first, double? second)
    {
        return (first, second) switch
        {
            ({ } left, { } right) => (left + right) / 2d,
            ({ } left, null) => left,
            (null, { } right) => right,
            _ => null
        };
    }

    private static ushort? CombineStatus(ushort? first, ushort? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return (ushort)(first.Value | second.Value);
    }
}
