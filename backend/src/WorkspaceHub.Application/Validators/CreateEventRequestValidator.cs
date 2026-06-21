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
        RuleFor(x => x.End).NotEmpty().WithMessage("End time is required.")
            .GreaterThan(x => x.Start).WithMessage("End time must be after Start time.");

        RuleFor(x => x.Attendees)
            .Must(attendees => attendees!.All(a => !string.IsNullOrWhiteSpace(a)))
            .When(x => x.Attendees != null)
            .WithMessage("Attendees list cannot contain empty emails.");
    }
}
