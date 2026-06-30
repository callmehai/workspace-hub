using FluentValidation;
using System;
using WorkspaceHub.Application.DTOs.ScheduledEmails;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validator for CreateScheduledEmailRequest.
/// </summary>
public class CreateScheduledEmailRequestValidator : AbstractValidator<CreateScheduledEmailRequest>
{
    public CreateScheduledEmailRequestValidator()
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

        RuleFor(x => x.SendAt)
            .Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("Thời gian gửi phải là UTC (thêm 'Z' vào cuối, ví dụ: 2026-06-22T15:00:00Z).");

        RuleFor(x => x.SendAt)
            .Must(sendAt => sendAt > DateTime.UtcNow.AddMinutes(1))
            .WithMessage("Thời gian gửi phải sau thời điểm hiện tại ít nhất 1 phút.");
    }
}
