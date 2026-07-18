using FluentAssertions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Validators;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests;

public class EventReminderDtoValidatorTests
{
    private readonly EventReminderDtoValidator _validator = new();

    private static EventReminderDto Reminder(int offsetValue, ReminderUnit unit, string? timeOfDay = null)
        => new(null, ReminderType.InApp, offsetValue, unit, timeOfDay);

    [Theory]
    [InlineData(0, ReminderUnit.Minutes)]
    [InlineData(30, ReminderUnit.Minutes)]
    [InlineData(4, ReminderUnit.Weeks)]      // đúng 4 tuần = 40320 phút → hợp lệ
    [InlineData(28, ReminderUnit.Days)]
    public void WithinFourWeeks_Passes(int value, ReminderUnit unit)
    {
        _validator.Validate(Reminder(value, unit)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(5, ReminderUnit.Weeks)]      // 5 tuần > 4 tuần
    [InlineData(29, ReminderUnit.Days)]
    [InlineData(999, ReminderUnit.Weeks)]    // max cũ của FE
    public void ExceedsFourWeeks_Fails(int value, ReminderUnit unit)
    {
        _validator.Validate(Reminder(value, unit)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void NegativeOffset_Fails()
    {
        _validator.Validate(Reminder(-1, ReminderUnit.Minutes)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("09:00", true)]
    [InlineData("23:59", true)]
    [InlineData("24:00", false)]
    [InlineData("9:0", false)]
    public void TimeOfDayFormat_Validated(string timeOfDay, bool expectedValid)
    {
        _validator.Validate(Reminder(1, ReminderUnit.Days, timeOfDay)).IsValid.Should().Be(expectedValid);
    }
}
