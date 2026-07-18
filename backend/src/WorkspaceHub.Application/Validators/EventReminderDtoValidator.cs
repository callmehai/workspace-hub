using FluentValidation;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validate 1 reminder: lead time không âm và tối đa 4 tuần (40320 phút) — khớp giới hạn
/// thật của Google Calendar (reminder xa hơn Google từ chối), đồng thời cho worker bound
/// query theo horizon cố định (xem EventReminderProcessorService).
/// </summary>
public class EventReminderDtoValidator : AbstractValidator<EventReminderDto>
{
    public const int MaxLeadMinutes = 40320; // 4 tuần

    public EventReminderDtoValidator()
    {
        RuleFor(x => x.OffsetValue)
            .GreaterThanOrEqualTo(0).WithMessage("Reminder offset cannot be negative.");

        RuleFor(x => x)
            .Must(r => LeadMinutes(r.OffsetValue, r.OffsetUnit) <= MaxLeadMinutes)
            .WithMessage("Reminder lead time cannot exceed 4 weeks.");

        RuleFor(x => x.TimeOfDay)
            .Matches(@"^([01]\d|2[0-3]):[0-5]\d$")
            .When(x => !string.IsNullOrEmpty(x.TimeOfDay))
            .WithMessage("TimeOfDay must be in HH:mm format.");
    }

    public static long LeadMinutes(int offsetValue, ReminderUnit unit) => unit switch
    {
        ReminderUnit.Minutes => offsetValue,
        ReminderUnit.Hours => (long)offsetValue * 60,
        ReminderUnit.Days => (long)offsetValue * 1440,
        ReminderUnit.Weeks => (long)offsetValue * 10080,
        _ => offsetValue
    };
}
