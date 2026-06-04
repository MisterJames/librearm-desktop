namespace LibreArm.Core.Models;

public enum BiologicalSex
{
    Unspecified,
    Female,
    Male
}

public static class BiologicalSexExtensions
{
    public static string ToDisplayText(this BiologicalSex sex)
    {
        return sex switch
        {
            BiologicalSex.Female => "Female",
            BiologicalSex.Male => "Male",
            _ => "Unspecified"
        };
    }
}
