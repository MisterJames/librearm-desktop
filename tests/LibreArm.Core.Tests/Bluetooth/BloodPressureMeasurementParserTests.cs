namespace LibreArm.Core.Tests.Bluetooth;

using LibreArm.Core.Bluetooth;
using LibreArm.Core.Models;

[TestClass]
public sealed class BloodPressureMeasurementParserTests
{
    [TestMethod]
    public void Parse_ReadsMandatoryMmhgValues()
    {
        byte[] payload = [0x00, 0x78, 0x00, 0x50, 0x00, 0x5D, 0x00];

        var reading = BloodPressureMeasurementParser.Parse(payload, new DateTimeOffset(2026, 6, 4, 8, 0, 0, TimeSpan.Zero));

        Assert.AreEqual(BloodPressureUnit.MillimetersOfMercury, reading.Unit);
        Assert.AreEqual(120, reading.Systolic);
        Assert.AreEqual(80, reading.Diastolic);
        Assert.AreEqual(93, reading.MeanArterialPressure);
        Assert.IsNull(reading.PulseRate);
        Assert.AreEqual("Not reported", reading.MeasurementStatusText);
    }

    [TestMethod]
    public void Parse_ReadsTimestampPulseAndStatus()
    {
        byte[] payload =
        [
            0x16,
            0x78, 0x00,
            0x50, 0x00,
            0x5D, 0x00,
            0xEA, 0x07, 0x06, 0x04, 0x08, 0x1E, 0x00,
            0x48, 0x00,
            0x00, 0x00
        ];

        var reading = BloodPressureMeasurementParser.Parse(payload);

        Assert.AreEqual(new DateTime(2026, 6, 4, 8, 30, 0), reading.Timestamp);
        Assert.AreEqual(72, reading.PulseRate);
        Assert.AreEqual((ushort?)0, reading.MeasurementStatus);
        Assert.AreEqual("Normal", reading.MeasurementStatusText);
    }

    [TestMethod]
    public void Parse_ReadsKpaSfloatValues()
    {
        byte[] payload =
        [
            0x01,
            0xA0, 0xF0,
            0xAA, 0xF0,
            0xC8, 0xF0
        ];

        var reading = BloodPressureMeasurementParser.Parse(payload);

        Assert.AreEqual(BloodPressureUnit.Kilopascals, reading.Unit);
        Assert.AreEqual(16.0, reading.Systolic, 0.001);
        Assert.AreEqual(17.0, reading.Diastolic, 0.001);
        Assert.AreEqual(20.0, reading.MeanArterialPressure, 0.001);
    }

    [TestMethod]
    public void Parse_ThrowsWhenFlaggedFieldsAreMissing()
    {
        byte[] payload = [0x16, 0x78, 0x00, 0x50, 0x00, 0x5D, 0x00, 0xEA, 0x07];

        Assert.ThrowsExactly<BloodPressureParseException>(() => BloodPressureMeasurementParser.Parse(payload));
    }
}
