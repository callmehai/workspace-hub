using FluentValidation;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs.Drive;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validate body PUT /api/drive/items/{itemId}/link-sharing (400).
/// Tắt link (Enabled=false) không cần Role — chỉ xóa permission anyone trên Google.
/// Bật link (Enabled=true) bắt buộc Role: reader | commenter | writer.
/// </summary>
public class LinkSharingRequestValidator : AbstractValidator<LinkSharingRequest>
{
    public LinkSharingRequestValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.Role)
                .NotEmpty()
                .WithMessage("Role là bắt buộc khi bật link chia sẻ.")
                .Must(role => DrivePermissionRoles.TryParse(role, out _))
                .WithMessage("Role phải là reader, commenter hoặc writer.");
        });
    }
}
