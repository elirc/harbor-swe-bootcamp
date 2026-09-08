namespace Harbor.Domain;

/// <summary>UTC half-open intervals expressed as minutes after midnight; Sunday = 0.</summary>
public sealed record WorkingInterval(DayOfWeek Day, int StartMinute, int EndMinute);
public sealed record WorkingCalendarDefinition(IReadOnlyList<WorkingInterval> Intervals, IReadOnlyList<DateOnly> ClosedDates);

public static class WorkingTime
{
    public static WorkingCalendarDefinition Normalize(WorkingCalendarDefinition definition)
    {
        if (definition.Intervals is null || definition.ClosedDates is null || definition.Intervals.Count is < 1 or > 28
            || definition.ClosedDates.Count > 100 || definition.Intervals.Any(i => i is null))
            throw new DomainException("Provide 1–28 valid UTC windows and at most 100 closed dates.");
        var intervals = definition.Intervals.OrderBy(i => i.Day).ThenBy(i => i.StartMinute).ToArray();
        if (intervals.Any(i => !Enum.IsDefined(i.Day) || i.StartMinute < 0 || i.EndMinute > 1440 || i.StartMinute >= i.EndMinute))
            throw new DomainException("Working windows must have a valid weekday and increasing minutes within 0–1440.");
        foreach (var day in intervals.GroupBy(i => i.Day))
        {
            if (day.Count() > 4) throw new DomainException("At most four working windows per UTC day.");
            var previousEnd = -1;
            foreach (var interval in day)
            {
                if (interval.StartMinute < previousEnd) throw new DomainException("Working windows cannot overlap.");
                previousEnd = interval.EndMinute;
            }
        }
        return new(intervals, definition.ClosedDates.Distinct().Order().ToArray());
    }

    public static DateTimeOffset AddWorkingDuration(DateTimeOffset start, int minutes, WorkingCalendarDefinition definition)
    {
        if (minutes < 1) throw new DomainException("Working duration must be positive.");
        var calendar = Normalize(definition);
        start = start.ToUniversalTime();
        try
        {
            var horizon = start.AddDays(366);
            var day = new DateTimeOffset(start.Year, start.Month, start.Day, 0, 0, 0, TimeSpan.Zero);
            var remaining = checked((long)minutes * TimeSpan.TicksPerMinute);
            var closures = calendar.ClosedDates.ToHashSet();
            while (day < horizon)
            {
                if (!closures.Contains(DateOnly.FromDateTime(day.UtcDateTime)))
                {
                    foreach (var window in calendar.Intervals.Where(i => i.Day == day.DayOfWeek))
                    {
                        var opening = day.AddMinutes(window.StartMinute);
                        var closing = day.AddMinutes(window.EndMinute);
                        if (closing > horizon) closing = horizon;
                        var cursor = opening < start ? start : opening;
                        if (cursor >= closing) continue;
                        var available = (closing - cursor).Ticks;
                        if (remaining <= available) return cursor.AddTicks(remaining);
                        remaining -= available;
                    }
                }
                day = day.AddDays(1);
            }
        }
        catch (ArgumentOutOfRangeException) { throw new DomainException("Working target exceeds the supported date range."); }
        throw new DomainException("Working target cannot be reached within 366 days.");
    }
}
