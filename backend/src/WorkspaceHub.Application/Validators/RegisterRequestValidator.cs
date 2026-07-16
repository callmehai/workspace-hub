using FluentValidation;
using WorkspaceHub.Application.DTOs.Auth;

namespace WorkspaceHub.Application.Validators;

/// <summary>Validate input đăng ký: email hợp lệ, password ≥ 8, fullName bắt buộc.</summary>
public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Must be a valid email address");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(200).WithMessage("Full name must not exceed 200 characters");

        // SCRUM-64: OTP xác minh qua email — không còn field phone.
    }
}
