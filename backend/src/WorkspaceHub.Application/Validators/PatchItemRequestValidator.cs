using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

public class PatchItemRequestValidator : AbstractValidator<PatchItemRequest>
{
    public PatchItemRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.IsUnread.HasValue || x.IsStarred.HasValue || x.Labels != null ||
                       x.IsTrashed.HasValue || x.Title != null || x.Start.HasValue ||
                       x.End.HasValue || x.Location != null || x.Attendees != null ||
                       x.Name != null)
            .WithMessage("Request body must contain at least one field to update.");

        RuleFor(x => x.Start)
            .LessThan(x => x.End)
            .When(x => x.Start.HasValue && x.End.HasValue)
            .WithMessage("Start time must be before End time.");

        RuleFor(x => x.Attendees)
            .Must(attendees => attendees!.All(a => !string.IsNullOrWhiteSpace(a)))
            .When(x => x.Attendees != null)
            .WithMessage("Attendees list cannot contain empty emails.");

        RuleFor(x => x.Name)
            .Must(name => name != null && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0)
            .When(x => x.Name != null)
            .WithMessage("Name contains invalid characters.");
    }
}
