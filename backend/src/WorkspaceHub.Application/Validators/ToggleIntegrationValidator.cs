using FluentValidation;
using WorkspaceHub.Application.DTOs.Connections;

namespace WorkspaceHub.Application.Validators;

public class ToggleIntegrationValidator : AbstractValidator<ToggleIntegrationRequest>
{
    public ToggleIntegrationValidator()
    {
        RuleFor(x => x.IsEnabled).NotNull();
    }
}
