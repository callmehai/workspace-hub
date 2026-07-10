using FluentValidation;
using WorkspaceHub.Application.DTOs.Contacts;

namespace WorkspaceHub.Application.Validators;

public class PatchContactRequestValidator : AbstractValidator<PatchContactRequest>
{
    public PatchContactRequestValidator()
    {
        RuleFor(x => x.Etag)
            .NotEmpty().WithMessage("Etag is required for PATCH.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Email must be a valid address.")
            .MaximumLength(320).WithMessage("Email must be at most 320 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.DisplayName)
            .MaximumLength(256).WithMessage("DisplayName must be at most 256 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.DisplayName));

        When(x => x.Profile != null, () =>
        {
            RuleFor(x => x.Profile!).SetValidator(new ContactProfileValidator());
            RuleFor(x => x.Profile!)
                .Must(p => p.Emails.Any(e => !string.IsNullOrWhiteSpace(e.Value)))
                .WithMessage("Contact must have at least one email.");
        });

        RuleFor(x => x)
            .Must(x => x.Profile != null || !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.DisplayName))
            .WithMessage("At least one of Profile, Email, or DisplayName must be provided.");
    }
}
