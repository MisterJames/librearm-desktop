namespace LibreArm.Core.Bluetooth;

public sealed class BloodPressureParseException : Exception
{
    public BloodPressureParseException(string message)
        : base(message)
    {
    }
}
