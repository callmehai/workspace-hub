using FluentValidation;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validator cho POST /api/folders/{id}/shares.
/// Gọi ValidateAndThrowAsync trong service (không phải controller) — CONVENTIONS.md.
/// </summary>
public class InviteFolderShareRequestValidator : AbstractValidator<InviteFolderShareRequest>
{
    public InviteFolderShareRequestValidator()
    {
        RuleFor(x => x.FriendUserId)
            .NotEmpty()
            .WithMessage("FriendUserId không được để trống.");

        RuleFor(x => x.Permission)
            .NotEmpty()
            .WithMessage("Permission không được để trống.")
            .Must(p => Enum.TryParse<SharePermission>(p, ignoreCase: true, out _))
            .WithMessage($"Permission phải là một trong: {string.Join(", ", Enum.GetNames<SharePermission>())}.");
    }
}
