namespace LibreArm.Core.Models;

public static class BloodPressureClassifier
{
    public static BloodPressureCategoryResult Classify(BloodPressureReading reading, UserProfile profile)
    {
        if (profile.Age < 18)
        {
            return new BloodPressureCategoryResult(
                BloodPressureCategory.PediatricUnsupported,
                "Pediatric unsupported",
                "Muted",
                "Adult AHA categories are not applied under age 18.",
                "LibreArm does not provide pediatric blood pressure interpretation.");
        }

        return ClassifyAdult(reading.Systolic, reading.Diastolic);
    }

    public static BloodPressureCategoryResult ClassifyAdult(double systolic, double diastolic)
    {
        if (systolic > 180 || diastolic > 120)
        {
            return Result(BloodPressureCategory.SevereHypertension, "Severe hypertension", "Danger");
        }

        if (systolic >= 140 || diastolic >= 90)
        {
            return Result(BloodPressureCategory.Stage2Hypertension, "Stage 2 hypertension", "Danger");
        }

        if ((systolic >= 130 && systolic <= 139) || (diastolic >= 80 && diastolic <= 89))
        {
            return Result(BloodPressureCategory.Stage1Hypertension, "Stage 1 hypertension", "Warning");
        }

        if (systolic >= 120 && systolic <= 129 && diastolic < 80)
        {
            return Result(BloodPressureCategory.Elevated, "Elevated", "Caution");
        }

        if (systolic < 120 && diastolic < 80)
        {
            return Result(BloodPressureCategory.Normal, "Normal", "Good");
        }

        return Result(BloodPressureCategory.Stage1Hypertension, "Stage 1 hypertension", "Warning");
    }

    private static BloodPressureCategoryResult Result(BloodPressureCategory category, string label, string colorKey)
    {
        return new BloodPressureCategoryResult(
            category,
            label,
            colorKey,
            "AHA adult reference: below 120/80",
            "Only a qualified health professional can diagnose high blood pressure or set your personal target.");
    }
}
