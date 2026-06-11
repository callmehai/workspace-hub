using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

/// <summary>
/// Validation cho query parameters của GET /api/items.
/// API.md: "Pagination: ?page=1&limit=20. Default 20, max 100."
/// </summary>
public class GetItemsRequestValidator : AbstractValidator<GetItemsRequest>
{
    public GetItemsRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be at least 1.");

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100)
            .WithMessage("Limit must be between 1 and 100.");

        RuleFor(x => x.Search)
            .MaximumLength(200)
            .WithMessage("Search term cannot exceed 200 characters.")
            .When(x => x.Search is not null);
    }
}
