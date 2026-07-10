using FluentValidation;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs.Drive;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validate body POST /api/drive/items/{itemId}/permissions — email + role hợp lệ (400).
/// Email trùng (409) do service kiểm tra sau khi hỏi Google.
/// </summary>
public class AddDrivePermissionRequestValidator : AbstractValidator<AddDrivePermissionRequest>
{
    public AddDrivePermissionRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email là bắt buộc.")
            .EmailAddress()
            .WithMessage("'{PropertyValue}' không phải địa chỉ email hợp lệ.");

        RuleFor(x => x.Role)
            .NotEmpty()
            .WithMessage("Role là bắt buộc.")
            .Must(role => DrivePermissionRoles.TryParse(role, out _))
            .WithMessage("Role phải là reader, commenter hoặc writer.");
    }
}
