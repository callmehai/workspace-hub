using FluentValidation;
using System;
using WorkspaceHub.Application.DTOs.ScheduledEmails;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validator for CreateScheduledEmailRequest.
/// </summary>
public class CreateScheduledEmailRequestValidator : AbstractValidator<CreateScheduledEmailRequest>
{
    public CreateScheduledEmailRequestValidator()
    {
        RuleFor(x => x.ConnectionId)
            .NotEmpty().WithMessage("ConnectionId is required.");

        RuleFor(x => x.To)
            .NotEmpty().WithMessage("At least one recipient (To) is required.");

        RuleForEach(x => x.To)
            .NotEmpty().WithMessage("Recipient email cannot be empty.")
            .EmailAddress().WithMessage("Invalid email address format in 'To'.");

        RuleForEach(x => x.Cc)
            .NotEmpty().WithMessage("CC email cannot be empty.")
            .EmailAddress().WithMessage("Invalid email address format in 'Cc'.");

        RuleForEach(x => x.Bcc)
            .NotEmpty().WithMessage("BCC email cannot be empty.")
            .EmailAddress().WithMessage("Invalid email address format in 'Bcc'.");

        RuleFor(x => x.Subject)
            .NotEmpty().WithMessage("Subject is required.")
            .MaximumLength(500).WithMessage("Subject must not exceed 500 characters.");

        RuleFor(x => x.BodyHtml)
            .NotEmpty().WithMessage("BodyHtml is required.");

        RuleFor(x => x.SendAt)
            .Must(sendAt => sendAt > DateTime.UtcNow)
            .WithMessage("SendAt must be in the future.");
    }
}
