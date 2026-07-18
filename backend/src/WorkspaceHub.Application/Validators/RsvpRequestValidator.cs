using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

public class RsvpRequestValidator : AbstractValidator<RsvpRequest>
{
    public RsvpRequestValidator()
    {
        RuleFor(x => x.Response)
            .NotEmpty().WithMessage("RSVP response is required.")
            .Must(r => r == "accepted" || r == "declined" || r == "tentative" || r == "tentative" || r == "needsAction")
            .WithMessage("RSVP response must be accepted, declined, tentative, or needsAction.");
    }
}
