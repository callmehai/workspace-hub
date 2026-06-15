using FluentValidation;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Validators;

public class UpdateItemStatusValidator : AbstractValidator<UpdateItemStatusRequest>
{
    public UpdateItemStatusValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum()
            .WithMessage("Status must be a valid ItemStatus (Inbox, Doing, Done).");
    }
}
