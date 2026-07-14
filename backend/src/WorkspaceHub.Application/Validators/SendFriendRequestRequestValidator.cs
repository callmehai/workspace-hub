using FluentValidation;
using WorkspaceHub.Application.DTOs.Friends;

namespace WorkspaceHub.Application.Validators;

public class SendFriendRequestRequestValidator : AbstractValidator<SendFriendRequestRequest>
{
    public SendFriendRequestRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email là bắt buộc.")
            .EmailAddress().WithMessage("Email không hợp lệ.")
            .MaximumLength(256);
    }
}

public class UpdateFriendTierRequestValidator : AbstractValidator<UpdateFriendTierRequest>
{
    private static readonly string[] AllowedTiers = { "Friend", "CloseFriend" };

    public UpdateFriendTierRequestValidator()
    {
        RuleFor(x => x.Tier)
            .NotEmpty()
            .Must(t => AllowedTiers.Contains(t, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Tier phải là Friend hoặc CloseFriend.");
    }
}
