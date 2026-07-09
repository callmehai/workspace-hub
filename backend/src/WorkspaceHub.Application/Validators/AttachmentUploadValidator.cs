using FluentValidation;
using WorkspaceHub.Application.DTOs.Emails;

namespace WorkspaceHub.Application.Validators;

/// <summary>Validate từng file người dùng đính kèm (tên + nội dung base64).</summary>
public class AttachmentUploadValidator : AbstractValidator<AttachmentUpload>
{
    public AttachmentUploadValidator()
    {
        RuleFor(x => x.Filename)
            .NotEmpty().WithMessage("Tên file đính kèm không được để trống.")
            .MaximumLength(255).WithMessage("Tên file không được vượt quá 255 ký tự.");

        RuleFor(x => x.ContentBase64)
            .NotEmpty().WithMessage("Nội dung file đính kèm không được để trống.");
    }
}

/// <summary>Quy tắc dung lượng đính kèm dùng chung cho Send/Reply/Forward (giới hạn Gmail ~25MB/thư).</summary>
public static class AttachmentRules
{
    public const long MaxTotalBytes = 25L * 1024 * 1024;

    /// <summary>Ước lượng tổng dung lượng đã decode từ độ dài base64 (≈ len * 3/4), không cần decode thật.</summary>
    public static long EstimateTotalBytes(IEnumerable<AttachmentUpload>? attachments)
    {
        if (attachments == null) return 0;
        long total = 0;
        foreach (var a in attachments)
        {
            var len = a.ContentBase64?.Length ?? 0;
            total += len / 4 * 3;
        }
        return total;
    }
}
