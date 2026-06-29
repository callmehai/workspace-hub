using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

public class PatchItemRequestValidator : AbstractValidator<PatchItemRequest>
{
    public PatchItemRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.IsUnread.HasValue || x.IsStarred.HasValue || x.AddLabels != null || x.RemoveLabels != null ||
                       x.IsTrashed.HasValue || x.Title != null || x.Start.HasValue ||
                       x.End.HasValue || x.Location != null || x.Attendees != null ||
                       x.Name != null ||
                       // Jira (SCRUM-57)
                       x.Summary != null || x.Description != null || x.Assignee != null ||
                       x.Priority != null || x.StatusTransition != null || x.Labels != null || x.Comment != null)
            .WithMessage("Request body must contain at least one field to update.");

        RuleFor(x => x.Start)
            .LessThan(x => x.End)
            .When(x => x.Start.HasValue && x.End.HasValue)
            .WithMessage("Start time must be before End time.");

        RuleFor(x => x.AddLabels)
            .Must(labels => labels!.All(l => !string.IsNullOrWhiteSpace(l)))
            .When(x => x.AddLabels != null)
            .WithMessage("AddLabels list cannot contain empty label IDs.");

        RuleFor(x => x.RemoveLabels)
            .Must(labels => labels!.All(l => !string.IsNullOrWhiteSpace(l)))
            .When(x => x.RemoveLabels != null)
            .WithMessage("RemoveLabels list cannot contain empty label IDs.");

        RuleFor(x => x.Attendees)
            .Must(attendees => attendees!.All(a => !string.IsNullOrWhiteSpace(a)))
            .When(x => x.Attendees != null)
            .WithMessage("Attendees list cannot contain empty emails.");

        RuleFor(x => x.Name)
            .Must(name => name != null && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0)
            .When(x => x.Name != null)
            .WithMessage("Name contains invalid characters.");

        // Jira (SCRUM-57): summary không rỗng nếu có gửi; label không chứa khoảng trắng.
        RuleFor(x => x.Summary)
            .NotEmpty().When(x => x.Summary != null)
            .WithMessage("Summary cannot be empty.")
            .MaximumLength(255).When(x => x.Summary != null)
            .WithMessage("Summary must be at most 255 characters.");

        RuleFor(x => x.Labels)
            .Must(labels => labels!.All(l => !string.IsNullOrWhiteSpace(l) && !l.Any(char.IsWhiteSpace)))
            .When(x => x.Labels != null)
            .WithMessage("Labels cannot be empty or contain whitespace.");
    }
}
