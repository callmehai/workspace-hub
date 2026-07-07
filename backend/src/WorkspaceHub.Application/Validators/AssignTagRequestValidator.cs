using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

public class AssignTagRequestValidator : AbstractValidator<AssignTagRequest>
{
    public AssignTagRequestValidator()
    {
        RuleFor(x => x.ItemId)
            .NotEmpty().WithMessage("ItemId is required.");
    }
}
