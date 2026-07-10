using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

public class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty().WithMessage("ConnectionId cannot be empty.");
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title cannot be empty.");
        RuleFor(x => x.Start).NotEmpty().WithMessage("Start time is required.");

        // Task: End không bắt buộc (service tự set End = Start.AddDays(1)).
        // Event: End bắt buộc và phải sau Start.
        When(x => string.IsNullOrEmpty(x.CalendarType) || x.CalendarType.Equals("event", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.End)
                .NotEmpty().WithMessage("End time is required for events.")
                .GreaterThan(x => x.Start).WithMessage("End time must be after Start time.");
        });

        RuleFor(x => x.Attendees)
            .Must(attendees => attendees!.All(a => !string.IsNullOrWhiteSpace(a)))
            .When(x => x.Attendees != null)
            .WithMessage("Attendees list cannot contain empty emails.");

        RuleFor(x => x.CalendarType)
            .Must(ct => ct == null || ct == "event" || ct == "task")
            .WithMessage("CalendarType must be 'event' or 'task'.");

        // Task không được có location/attendees (rejected by service, but also validate here)
        RuleFor(x => x.DriveItemIds)
            .Must(ids => ids == null || ids.Count <= 20)
            .WithMessage("Maximum 20 Drive files can be attached.");
    }
}
