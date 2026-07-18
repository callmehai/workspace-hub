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

        RuleFor(x => x.End)
            .NotEmpty().WithMessage("End time is required.")
            .GreaterThan(x => x.Start).WithMessage("End time must be after Start time.");

        RuleFor(x => x.Attendees)
            .Must(attendees => attendees!.Count <= 200 && attendees.All(a => !string.IsNullOrWhiteSpace(a)))
            .When(x => x.Attendees != null)
            .WithMessage("Attendees must contain at most 200 non-empty emails.");

        RuleForEach(x => x.Attendees)
            .EmailAddress().WithMessage("Each attendee must be a valid email address.")
            .When(x => x.Attendees != null);

        RuleFor(x => x.DriveItemIds)
            .Must(ids => ids == null || ids.Count <= 20)
            .WithMessage("Maximum 20 Drive files can be attached.");

        RuleForEach(x => x.Reminders)
            .SetValidator(new EventReminderDtoValidator())
            .When(x => x.Reminders != null);
    }
}
