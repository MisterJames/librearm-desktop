namespace LibreArm.Core.Models;

public sealed record UserProfile(
    long Id,
    string DisplayName,
    DateOnly BirthDate,
    BiologicalSex BiologicalSex,
    string? PhotoPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim()[0].ToString().ToUpperInvariant();

    public int Age
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var age = today.Year - BirthDate.Year;
            if (BirthDate > today.AddYears(-age))
            {
                age--;
            }

            return age;
        }
    }

    public string DemographicsText => $"{Age} years, {BiologicalSex.ToDisplayText()}";
}
