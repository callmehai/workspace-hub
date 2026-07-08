using FluentValidation;
using WorkspaceHub.Application.DTOs.Emails;

namespace WorkspaceHub.Application.Validators;

public class ReplyEmailRequestValidator : AbstractValidator<ReplyEmailRequest>
{
    public ReplyEmailRequestValidator()
    {
        RuleFor(x => x.ConnectionId)
            .NotEmpty().WithMessage("Vui lòng chọn kết nối Gmail.");

        RuleFor(x => x.ItemId)
            .NotEmpty().WithMessage("Vui lòng chọn email cần reply.");

        RuleFor(x => x.BodyHtml)
            .NotEmpty().WithMessage("Vui lòng nhập nội dung reply.");

        RuleForEach(x => x.Cc)
            .NotEmpty().WithMessage("Địa chỉ email CC không được để trống.")
            .EmailAddress().WithMessage("'{PropertyValue}' không phải địa chỉ email hợp lệ trong trường 'Cc'.");

        RuleForEach(x => x.Bcc)
            .NotEmpty().WithMessage("Địa chỉ email BCC không được để trống.")
            .EmailAddress().WithMessage("'{PropertyValue}' không phải địa chỉ email hợp lệ trong trường 'Bcc'.");
    }
}
