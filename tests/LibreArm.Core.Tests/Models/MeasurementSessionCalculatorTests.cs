namespace LibreArm.Core.Tests.Models;

using LibreArm.Core.Models;

[TestClass]
public sealed class MeasurementSessionCalculatorTests
{
    [TestMethod]
    public void Average_AveragesBloodPressureAndPulseValues()
    {
        var first = Reading(120, 80, 93, 70, 0x0001);
        var second = Reading(126, 84, 98, 74, 0x0004);

        var average = MeasurementSessionCalculator.Average(first, second);

        Assert.AreEqual(123, average.Systolic);
        Assert.AreEqual(82, average.Diastolic);
        Assert.AreEqual(95.5, average.MeanArterialPressure);
        Assert.AreEqual(72, average.PulseRate);
        Assert.AreEqual((ushort?)0x0005, average.MeasurementStatus);
    }

    [TestMethod]
    public void Average_UsesAvailablePulseWhenOnlyOneReadingHasPulse()
    {
        var first = Reading(120, 80, 93, null, null);
        var second = Reading(124, 82, 96, 72, null);

        var average = MeasurementSessionCalculator.Average(first, second);

        Assert.AreEqual(72, average.PulseRate);
    }

    private static BloodPressureReading Reading(double systolic, double diastolic, double map, double? pulse, ushort? status)
    {
        return new BloodPressureReading(
            systolic,
            diastolic,
            map,
            BloodPressureUnit.MillimetersOfMercury,
            pulse,
            new DateTime(2026, 6, 4, 8, 0, 0),
            null,
            status,
            new DateTimeOffset(2026, 6, 4, 8, 0, 0, TimeSpan.Zero),
            []);
    }
}
