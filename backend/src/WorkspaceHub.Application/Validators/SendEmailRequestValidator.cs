using FluentValidation;
using WorkspaceHub.Application.DTOs.Emails;

namespace WorkspaceHub.Application.Validators;

public class SendEmailRequestValidator : AbstractValidator<SendEmailRequest>
{
    public SendEmailRequestValidator()
    {
        RuleFor(x => x.ConnectionId)
            .NotEmpty().WithMessage("Vui lòng chọn kết nối Gmail.");

        RuleFor(x => x.To)
            .NotEmpty().WithMessage("Vui lòng nhập ít nhất một người nhận (To).");

        RuleForEach(x => x.To)
            .NotEmpty().WithMessage("Địa chỉ email người nhận không được để trống.")
            .EmailAddress().WithMessage("'{PropertyValue}' không phải địa chỉ email hợp lệ trong trường 'To'.");

        RuleForEach(x => x.Cc)
            .NotEmpty().WithMessage("Địa chỉ email CC không được để trống.")
            .EmailAddress().WithMessage("'{PropertyValue}' không phải địa chỉ email hợp lệ trong trường 'Cc'.");

        RuleForEach(x => x.Bcc)
            .NotEmpty().WithMessage("Địa chỉ email BCC không được để trống.")
            .EmailAddress().WithMessage("'{PropertyValue}' không phải địa chỉ email hợp lệ trong trường 'Bcc'.");

        RuleFor(x => x.Subject)
            .NotEmpty().WithMessage("Vui lòng nhập tiêu đề email.")
            .MaximumLength(500).WithMessage("Tiêu đề không được vượt quá 500 ký tự.");

        RuleFor(x => x.BodyHtml)
            .NotEmpty().WithMessage("Vui lòng nhập nội dung email.");
    }
}
