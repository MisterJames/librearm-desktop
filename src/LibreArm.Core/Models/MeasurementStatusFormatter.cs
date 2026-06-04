namespace LibreArm.Core.Models;

public static class MeasurementStatusFormatter
{
    public static string Format(ushort? status)
    {
        if (status is null)
        {
            return "Not reported";
        }

        if (status == 0)
        {
            return "Normal";
        }

        var parts = new List<string>();
        AddIfSet(parts, status.Value, 0, "Body movement detected");
        AddIfSet(parts, status.Value, 1, "Cuff fit too loose");
        AddIfSet(parts, status.Value, 2, "Irregular pulse detected");
        AddIfSet(parts, status.Value, 3, "Pulse rate below lower limit");
        AddIfSet(parts, status.Value, 4, "Pulse rate above upper limit");
        AddIfSet(parts, status.Value, 5, "Improper measurement position");

        return parts.Count == 0 ? $"Unknown status 0x{status:X4}" : string.Join("; ", parts);
    }

    private static void AddIfSet(List<string> parts, ushort status, int bit, string label)
    {
        if ((status & (1 << bit)) != 0)
        {
            parts.Add(label);
        }
    }
}
