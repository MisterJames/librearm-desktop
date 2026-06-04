namespace LibreArm.Core.Bluetooth;

using LibreArm.Core.Models;

public static class BloodPressureMeasurementParser
{
    public static BloodPressureReading Parse(ReadOnlySpan<byte> payload, DateTimeOffset? receivedAt = null)
    {
        if (payload.Length < 7)
        {
            throw new BloodPressureParseException("Blood Pressure Measurement payload is too short.");
        }

        var index = 0;
        var flags = payload[index++];
        var unit = (flags & 0x01) == 0 ? BloodPressureUnit.MillimetersOfMercury : BloodPressureUnit.Kilopascals;
        var hasTimestamp = (flags & 0x02) != 0;
        var hasPulseRate = (flags & 0x04) != 0;
        var hasUserId = (flags & 0x08) != 0;
        var hasMeasurementStatus = (flags & 0x10) != 0;

        var systolic = ReadSfloat(payload, ref index);
        var diastolic = ReadSfloat(payload, ref index);
        var meanArterialPressure = ReadSfloat(payload, ref index);

        DateTime? timestamp = null;
        if (hasTimestamp)
        {
            EnsureAvailable(payload, index, 7);
            var year = ReadUInt16(payload, ref index);
            var month = payload[index++];
            var day = payload[index++];
            var hour = payload[index++];
            var minute = payload[index++];
            var second = payload[index++];

            if (year > 0 && month is >= 1 and <= 12 && day is >= 1 and <= 31)
            {
                timestamp = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            }
        }

        double? pulseRate = null;
        if (hasPulseRate)
        {
            pulseRate = ReadSfloat(payload, ref index);
        }

        byte? userId = null;
        if (hasUserId)
        {
            EnsureAvailable(payload, index, 1);
            userId = payload[index++];
        }

        ushort? status = null;
        if (hasMeasurementStatus)
        {
            status = ReadUInt16(payload, ref index);
        }

        if (index != payload.Length)
        {
            throw new BloodPressureParseException("Blood Pressure Measurement payload has unexpected trailing bytes.");
        }

        return new BloodPressureReading(
            systolic,
            diastolic,
            meanArterialPressure,
            unit,
            pulseRate,
            timestamp,
            userId,
            status,
            receivedAt ?? DateTimeOffset.Now,
            payload.ToArray());
    }

    private static double ReadSfloat(ReadOnlySpan<byte> payload, ref int index)
    {
        var raw = ReadUInt16(payload, ref index);

        return raw switch
        {
            0x07FE or 0x07FF => double.NaN,
            0x0800 => double.PositiveInfinity,
            0x0801 => double.NegativeInfinity,
            _ => DecodeSfloat(raw)
        };
    }

    private static double DecodeSfloat(ushort raw)
    {
        var mantissa = raw & 0x0FFF;
        if ((mantissa & 0x0800) != 0)
        {
            mantissa -= 0x1000;
        }

        var exponent = (raw >> 12) & 0x000F;
        if ((exponent & 0x0008) != 0)
        {
            exponent -= 0x0010;
        }

        return mantissa * Math.Pow(10, exponent);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> payload, ref int index)
    {
        EnsureAvailable(payload, index, 2);
        var value = (ushort)(payload[index] | (payload[index + 1] << 8));
        index += 2;
        return value;
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> payload, int index, int count)
    {
        if (payload.Length - index < count)
        {
            throw new BloodPressureParseException("Blood Pressure Measurement payload ended before all flagged fields were present.");
        }
    }
}
