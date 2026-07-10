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

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.DisplayName))
            .WithMessage("At least one of Email or DisplayName must be provided.");
    }
}
