using FluentValidation;
using WorkspaceHub.Application.DTOs.Drive;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validate body POST /api/drive/folders — format đầu vào (400).
/// Quy tắc nghiệp vụ (parent là folder, connection Active…) do <see cref="Services.DriveSharingService"/> xử lý (422).
/// </summary>
public class CreateDriveFolderRequestValidator : AbstractValidator<CreateDriveFolderRequest>
{
    public CreateDriveFolderRequestValidator()
    {
        RuleFor(x => x.ConnectionId)
            .NotEmpty()
            .WithMessage("Vui lòng chọn connection Google Drive.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tên folder không được để trống.")
            .MaximumLength(255)
            .WithMessage("Tên folder tối đa 255 ký tự.");
    }
}
