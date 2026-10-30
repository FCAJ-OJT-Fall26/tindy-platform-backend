using Tindy.Application.Entities;
using Tindy.Application.Exceptions;

namespace Tindy.Application.Services;

/// <summary>V1 calendar dates are in Vietnam; stored cutoffs are exclusive UTC instants.</summary>
public static class RecruitmentDeadline
{
    private static readonly TimeZoneInfo Vietnam = ResolveTimeZone();

    private static TimeZoneInfo ResolveTimeZone()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Ho_Chi_Minh", out var zone))
            return zone;
        return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
    }

    public static DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Vietnam));

    public static DateTime CreateCutoff(DateOnly? requestedDate, DateTime utcNow)
    {
        var today = LocalDate(utcNow);
        var date = requestedDate ?? today.AddDays(14);
        if (date <= today || date == DateOnly.MaxValue)
            throw new ProjectException(400, "VALIDATION_FAILED",
                "Recruitment closing date must be a future date (earliest tomorrow in Asia/Ho_Chi_Minh) before 9999-12-31.");

        var localMidnight = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, Vietnam);
    }

    // Midnight belongs to the preceding closing day. A migrated midday cutoff
    // retains that local day and its exact instant, rather than shifting a day.
    public static DateOnly ClosingDate(DateTime cutoff) => LocalDate(cutoff.AddTicks(-1));

    // Calendar days until the closing date: zero on the closing day and after
    // expiry. Use IsOpen/the exact cutoff to distinguish these cases.
    public static int DaysLeft(DateTime cutoff, DateTime utcNow) =>
        utcNow >= cutoff ? 0 : Math.Max(0, ClosingDate(cutoff).DayNumber - LocalDate(utcNow).DayNumber);

    public static bool IsOpen(Project project, DateTime utcNow) =>
        project.Status == "recruiting" && project.ArchivedAt is null && utcNow < project.RecruitmentClosesAt;
}
