using FluentAssertions;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests;

public class GoogleCalendarReminderMapperTests
{
    [Theory]
    [InlineData(ReminderUnit.Weeks, 1, "14:00", 9240)]   // 1 week before at 14:00
    [InlineData(ReminderUnit.Days, 1, "09:00", 900)]     // 1 day before at 09:00
    [InlineData(ReminderUnit.Weeks, 1, null, 10080)]     // 1 week before midnight
    [InlineData(ReminderUnit.Weeks, 1, "00:00", 10080)]
    [InlineData(ReminderUnit.Minutes, 30, "14:00", 30)]  // timed: ignore timeOfDay
    [InlineData(ReminderUnit.Hours, 2, "09:00", 120)]
    public void ToGoogleMinutes_MapsAllDayTimeOfDay(ReminderUnit unit, int offset, string? timeOfDay, int expected)
    {
        GoogleCalendarReminderMapper.ToGoogleMinutes(unit, offset, timeOfDay).Should().Be(expected);
    }

    [Theory]
    [InlineData(9240, true, 1, ReminderUnit.Weeks, "14:00")]
    [InlineData(900, true, 1, ReminderUnit.Days, "09:00")]
    [InlineData(10080, true, 1, ReminderUnit.Weeks, null)]
    [InlineData(30, false, 30, ReminderUnit.Minutes, null)]
    [InlineData(900, false, 900, ReminderUnit.Minutes, null)]
    public void FromGoogleMinutes_DecodesAllDayStyle(
        int minutes, bool allDay, int expectedOffset, ReminderUnit expectedUnit, string? expectedTod)
    {
        var (offset, unit, tod) = GoogleCalendarReminderMapper.FromGoogleMinutes(minutes, allDayStyle: allDay);
        offset.Should().Be(expectedOffset);
        unit.Should().Be(expectedUnit);
        tod.Should().Be(expectedTod);
    }

    [Fact]
    public void RoundTrip_OneWeekAt1400()
    {
        var minutes = GoogleCalendarReminderMapper.ToGoogleMinutes(ReminderUnit.Weeks, 1, "14:00");
        var decoded = GoogleCalendarReminderMapper.FromGoogleMinutes(minutes, allDayStyle: true);
        decoded.Should().Be((1, ReminderUnit.Weeks, "14:00"));
    }
}
