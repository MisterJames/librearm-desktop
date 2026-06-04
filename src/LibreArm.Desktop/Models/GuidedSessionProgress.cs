namespace LibreArm_Desktop.Models;

public sealed record GuidedSessionProgress(
    string Title,
    string Message,
    int? CountdownSeconds = null,
    string? Detail = null,
    bool IsComplete = false);
