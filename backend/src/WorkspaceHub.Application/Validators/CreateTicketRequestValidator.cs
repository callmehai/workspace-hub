using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

public class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty().WithMessage("ConnectionId cannot be empty.");
        RuleFor(x => x.ProjectKey).NotEmpty().WithMessage("ProjectKey is required.");
        RuleFor(x => x.IssueType).NotEmpty().WithMessage("IssueType is required.");
        RuleFor(x => x.Summary).NotEmpty().WithMessage("Summary is required.")
            .MaximumLength(255).WithMessage("Summary must be at most 255 characters.");

        // Jira label không cho phép khoảng trắng.
        RuleFor(x => x.Labels)
            .Must(labels => labels!.All(l => !string.IsNullOrWhiteSpace(l) && !l.Any(char.IsWhiteSpace)))
            .When(x => x.Labels != null)
            .WithMessage("Labels cannot be empty or contain whitespace.");
    }
}
