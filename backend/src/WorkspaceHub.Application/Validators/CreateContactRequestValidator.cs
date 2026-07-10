using FluentValidation;
using WorkspaceHub.Application.DTOs.Contacts;

namespace WorkspaceHub.Application.Validators;

public class CreateContactRequestValidator : AbstractValidator<CreateContactRequest>
{
    public CreateContactRequestValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty().WithMessage("ConnectionId is required.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid address.")
            .MaximumLength(320).WithMessage("Email must be at most 320 characters.");

        RuleFor(x => x.DisplayName)
            .MaximumLength(256).WithMessage("DisplayName must be at most 256 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.DisplayName));

        When(x => x.Profile != null, () =>
        {
            RuleFor(x => x.Profile!).SetValidator(new ContactProfileValidator());
        });
    }
}
