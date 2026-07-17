using FluentValidation;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validator cho PATCH /api/folders/{id}/shares/{shareId}.
/// Gọi ValidateAndThrowAsync trong service — CONVENTIONS.md.
/// </summary>
public class UpdateFolderShareRequestValidator : AbstractValidator<UpdateFolderShareRequest>
{
    public UpdateFolderShareRequestValidator()
    {
        RuleFor(x => x.Permission)
            .NotEmpty()
            .WithMessage("Permission không được để trống.")
            .Must(p => Enum.TryParse<SharePermission>(p, ignoreCase: true, out _))
            .WithMessage($"Permission phải là một trong: {string.Join(", ", Enum.GetNames<SharePermission>())}.");
    }
}
