using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// FluentValidation cho <see cref="CreateFolderRequest"/>.
/// Name required (max 200), Color hex hợp lệ, Icon required (max 100).
/// </summary>
public class CreateFolderValidator : AbstractValidator<CreateFolderRequest>
{
    public CreateFolderValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Folder name is required.")
            .MaximumLength(200).WithMessage("Folder name must not exceed 200 characters.");

        RuleFor(x => x.Color)
            .NotEmpty().WithMessage("Folder color is required.")
            .Matches(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$")
            .WithMessage("Color must be a valid hex format (#RGB or #RRGGBB).");

        RuleFor(x => x.Icon)
            .NotEmpty().WithMessage("Folder icon is required.")
            .MaximumLength(100).WithMessage("Folder icon must not exceed 100 characters.");
    }
}
