using FluentValidation;
using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Validators;

public class CreateNoteValidator : AbstractValidator<CreateNoteRequest>
{
    public CreateNoteValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.");

        RuleFor(x => x.ContentMarkdown)
            .NotEmpty().WithMessage("ContentMarkdown is required.")
            .MaximumLength(50000).WithMessage("ContentMarkdown cannot exceed 50000 characters.");

        RuleFor(x => x.FolderId)
            .NotEqual(Guid.Empty).When(x => x.FolderId.HasValue).WithMessage("FolderId cannot be empty GUID.");
    }
}
