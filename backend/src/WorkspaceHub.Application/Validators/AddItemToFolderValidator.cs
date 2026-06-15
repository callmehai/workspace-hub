using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

public class AddItemToFolderValidator : AbstractValidator<AddItemToFolderRequest>
{
    public AddItemToFolderValidator()
    {
        RuleFor(x => x.ItemId)
            .NotEmpty().WithMessage("ItemId is required.");
    }
}
