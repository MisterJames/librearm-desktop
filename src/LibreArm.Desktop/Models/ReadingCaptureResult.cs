namespace LibreArm_Desktop.Models;

using LibreArm.Core.Models;

public sealed record ReadingCaptureResult(BloodPressureReading Reading, int SampleCount);
