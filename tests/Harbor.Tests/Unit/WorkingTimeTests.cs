using Harbor.Domain;

namespace Harbor.Tests.Unit;

public class WorkingTimeTests
{
    private static WorkingCalendarDefinition Week(params DateOnly[] closed) => new(
        Enumerable.Range(1, 5).Select(d => new WorkingInterval((DayOfWeek)d, 540, 1020)).ToArray(), closed);

    [Fact]
    public void WeekendClosureAndFractionalSecondsConsumeOnlyWorkingTicks()
    {
        var friday = DateTimeOffset.Parse("2026-09-11T16:30:00Z");
        Assert.Equal(DateTimeOffset.Parse("2026-09-11T17:00:00Z"), WorkingTime.AddWorkingDuration(friday, 30, Week()));
        Assert.Equal(DateTimeOffset.Parse("2026-09-14T09:30:00Z"), WorkingTime.AddWorkingDuration(friday, 60, Week()));
        Assert.Equal(DateTimeOffset.Parse("2026-09-15T09:30:00Z"), WorkingTime.AddWorkingDuration(friday, 60, Week(new DateOnly(2026, 9, 14))));
        Assert.Equal(DateTimeOffset.Parse("2026-09-14T09:30:00.1234567Z"), WorkingTime.AddWorkingDuration(friday.AddTicks(1234567), 60, Week()));
        Assert.Equal(DateTimeOffset.Parse("2026-09-14T09:01:00Z"), WorkingTime.AddWorkingDuration(friday.AddMinutes(30), 1, Week()));
        Assert.Equal(DateTimeOffset.Parse("2026-09-11T09:01:00Z"), WorkingTime.AddWorkingDuration(friday.AddHours(-10), 1, Week()));
    }

    [Fact]
    public void InvalidWindowsAndUnreachableTargetsAreBoundedFailures()
    {
        Assert.Throws<DomainException>(() => WorkingTime.Normalize(new([], [])));
        Assert.Throws<DomainException>(() => WorkingTime.Normalize(new([null!], [])));
        Assert.Throws<DomainException>(() => WorkingTime.Normalize(new([new(DayOfWeek.Monday, 600, 600)], [])));
        Assert.Throws<DomainException>(() => WorkingTime.Normalize(new([new(DayOfWeek.Monday, 500, 700), new(DayOfWeek.Monday, 600, 800)], [])));
        var sparse = new WorkingCalendarDefinition([new(DayOfWeek.Monday, 540, 541)], []);
        Assert.Throws<DomainException>(() => WorkingTime.AddWorkingDuration(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 60, sparse));
        Assert.Throws<DomainException>(() => WorkingTime.AddWorkingDuration(DateTimeOffset.MaxValue, 1, Week()));
    }
}
