using FluentValidation;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Validators;

public class CreateImportantContactRequestValidator : AbstractValidator<CreateImportantContactRequest>
{
    public CreateImportantContactRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum().WithMessage("Type không hợp lệ.");

        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Identifier is required.")
            .MaximumLength(256).WithMessage("Identifier must be at most 256 characters.");

        // Email: identifier phải là email hợp lệ. JiraAccount: accountId tự do (chỉ cần non-empty).
        RuleFor(x => x.Identifier)
            .EmailAddress().WithMessage("Identifier must be a valid email for Email contacts.")
            .When(x => x.Type == ImportantContactType.Email);

        RuleFor(x => x.Label)
            .NotEmpty().WithMessage("Label is required.")
            .MaximumLength(200).WithMessage("Label must be at most 200 characters.");
    }
}
