using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// FluentValidation cho <see cref="UpdateFolderRequest"/>.
/// Same rules as Create + SortOrder ≥ 0.
/// </summary>
public class UpdateFolderValidator : AbstractValidator<UpdateFolderRequest>
{
    public UpdateFolderValidator()
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

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("SortOrder must be zero or positive.");
    }
}
