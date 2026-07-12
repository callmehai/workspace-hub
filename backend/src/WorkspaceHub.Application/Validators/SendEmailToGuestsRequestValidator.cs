using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

public class SendEmailToGuestsRequestValidator : AbstractValidator<SendEmailToGuestsRequest>
{
    public SendEmailToGuestsRequestValidator()
    {
        RuleFor(x => x.RecipientEmails)
            .NotEmpty().WithMessage("Recipient emails list cannot be empty.")
            .Must(emails => emails != null && emails.All(e => !string.IsNullOrWhiteSpace(e)))
            .WithMessage("Recipient emails cannot contain empty emails.");

        RuleFor(x => x.Subject)
            .NotEmpty().WithMessage("Subject cannot be empty.");

        RuleFor(x => x.BodyHtml)
            .NotEmpty().WithMessage("Email body cannot be empty.")
            .MaximumLength(5000).WithMessage("Email body is too long (max 5000 characters).");
    }
}
