using FluentValidation;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs.Drive;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validate body PATCH /api/drive/items/{itemId}/permissions/{permissionId} — role hợp lệ (400).
/// Không cho sửa owner do service xử lý (422).
/// </summary>
public class UpdateDrivePermissionRequestValidator : AbstractValidator<UpdateDrivePermissionRequest>
{
    public UpdateDrivePermissionRequestValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty()
            .WithMessage("Role là bắt buộc.")
            .Must(role => DrivePermissionRoles.TryParse(role, out _))
            .WithMessage("Role phải là reader, commenter hoặc writer.");
    }
}
