namespace LibreArm.Core.Models;

public sealed record BloodPressureCategoryResult(
    BloodPressureCategory Category,
    string DisplayLabel,
    string ColorKey,
    string TargetText,
    string DisclaimerText);
