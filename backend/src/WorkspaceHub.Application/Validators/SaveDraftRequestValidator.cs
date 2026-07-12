using FluentValidation;
using WorkspaceHub.Application.DTOs.Emails;

namespace WorkspaceHub.Application.Validators;

public class SaveDraftRequestValidator : AbstractValidator<SaveDraftRequest>
{
    public SaveDraftRequestValidator()
    {
        RuleFor(x => x.ConnectionId)
            .NotEmpty().WithMessage("Vui lòng chọn kết nối Gmail.");

        RuleForEach(x => x.To)
            .EmailAddress().WithMessage("'{PropertyValue}' không phải địa chỉ email hợp lệ trong trường 'To'.");

        RuleForEach(x => x.Cc)
            .EmailAddress().WithMessage("'{PropertyValue}' không phải địa chỉ email hợp lệ trong trường 'Cc'.");

        RuleForEach(x => x.Bcc)
            .EmailAddress().WithMessage("'{PropertyValue}' không phải địa chỉ email hợp lệ trong trường 'Bcc'.");

        RuleFor(x => x.Subject)
            .MaximumLength(500).WithMessage("Tiêu đề không được vượt quá 500 ký tự.");

        RuleForEach(x => x.Attachments).SetValidator(new AttachmentUploadValidator());
        RuleFor(x => x.Attachments)
            .Must(a => AttachmentRules.EstimateTotalBytes(a) <= AttachmentRules.MaxTotalBytes)
            .WithMessage("Tổng dung lượng đính kèm vượt quá 25MB (giới hạn Gmail).");
    }
}
