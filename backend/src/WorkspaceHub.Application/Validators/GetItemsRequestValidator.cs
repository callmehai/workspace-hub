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
            .InclusiveBetween(1, 200)
            .WithMessage("Limit must be between 1 and 200.");

        RuleFor(x => x.Search)
            .Must(s => s!.Trim().Length <= 200)
            .WithMessage("Search term cannot exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Search));

        RuleFor(x => x.OccurredTo)
            .GreaterThan(x => x.OccurredFrom)
            .When(x => x.OccurredFrom.HasValue && x.OccurredTo.HasValue)
            .WithMessage("occurredTo must be after occurredFrom.");
    }
}
