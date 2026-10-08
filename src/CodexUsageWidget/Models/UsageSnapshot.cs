namespace CodexUsageWidget.Models;

public sealed record UsageWindow(
    int RemainingPercent,
    DateTimeOffset? ResetsAt,
    int WindowDurationMinutes);

public sealed record UsageSnapshot(
    UsageWindow? FiveHour,
    UsageWindow? Weekly,
    DateTimeOffset CheckedAt);
