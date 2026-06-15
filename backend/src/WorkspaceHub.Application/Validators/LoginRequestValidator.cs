using FluentValidation;
using WorkspaceHub.Application.DTOs.Auth;

namespace WorkspaceHub.Application.Validators;

/// <summary>Validate input đăng nhập: email + password bắt buộc.</summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Must be a valid email address");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");
    }
}
