using FluentValidation;
using WorkspaceHub.Application.DTOs.Admin;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validation cho query parameters của GET /api/admin/users.
/// Rule: Page ≥ 1, Limit 1–100, Search ≤ 200 ký tự (trimmed, khi không null/whitespace).
/// Lý do không dùng MaxLength trực tiếp: cần trim trước khi đếm.
/// ExceptionMiddleware tự format 400 — KHÔNG tự format error trong controller.
/// </summary>
public class GetAdminUsersRequestValidator : AbstractValidator<GetAdminUsersRequest>
{
    public GetAdminUsersRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be at least 1.");

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100)
            .WithMessage("Limit must be between 1 and 100.");

        RuleFor(x => x.Search)
            .Must(s => s == null || s.Trim().Length <= 200)
            .WithMessage("Search term cannot exceed 200 characters.");
    }
}
