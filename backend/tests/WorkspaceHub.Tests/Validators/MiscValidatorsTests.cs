using FluentAssertions;
using WorkspaceHub.Application.DTOs.Admin;
using WorkspaceHub.Application.DTOs.Connections;
using WorkspaceHub.Application.DTOs.Friends;
using WorkspaceHub.Application.Validators;

namespace WorkspaceHub.Tests.Validators;

/// <summary>
/// Unit test cho nhóm validator còn lại: GetAdminUsers (query admin) / ToggleIntegration /
/// SendFriendRequest.
/// </summary>
public class MiscValidatorsTests
{
    // ── GetAdminUsersRequestValidator ─────────────────────────────────────

    private readonly GetAdminUsersRequestValidator _getAdminUsers = new();

    [Fact]
    public void Validate_GetAdminUsersDefaultRequest_ShouldPass()
    {
        // Default: page=1, limit=20, search=null.
        _getAdminUsers.Validate(new GetAdminUsersRequest()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_GetAdminUsersFullValidRequest_ShouldPass()
    {
        var request = new GetAdminUsersRequest("nguyen", 3, 100);
        _getAdminUsers.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_GetAdminUsersPageZero_ShouldFail()
    {
        var result = _getAdminUsers.Validate(new GetAdminUsersRequest(Page: 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Page");
    }

    [Fact]
    public void Validate_GetAdminUsersLimitZero_ShouldFail()
    {
        var result = _getAdminUsers.Validate(new GetAdminUsersRequest(Limit: 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Limit");
    }

    [Fact]
    public void Validate_GetAdminUsersLimitOver100_ShouldFail()
    {
        var result = _getAdminUsers.Validate(new GetAdminUsersRequest(Limit: 101));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Limit");
    }

    [Fact]
    public void Validate_GetAdminUsersSearchOver200Chars_ShouldFail()
    {
        var result = _getAdminUsers.Validate(new GetAdminUsersRequest(new string('s', 201)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Search");
    }

    [Fact]
    public void Validate_GetAdminUsersSearchPaddedTo200Chars_ShouldPass()
    {
        // Rule trim trước khi đếm → khoảng trắng thừa không tính.
        var request = new GetAdminUsersRequest("  " + new string('s', 200) + "  ");
        _getAdminUsers.Validate(request).IsValid.Should().BeTrue();
    }

    // ── ToggleIntegrationValidator ────────────────────────────────────────

    private readonly ToggleIntegrationValidator _toggleIntegration = new();

    [Fact]
    public void Validate_ToggleIntegrationEnabled_ShouldPass()
    {
        // Validator hiện KHÔNG khai báo rule nào (IsEnabled là bool, mọi giá trị đều hợp lệ).
        var request = new ToggleIntegrationRequest { IsEnabled = true };
        _toggleIntegration.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ToggleIntegrationDisabled_ShouldPass()
    {
        var request = new ToggleIntegrationRequest { IsEnabled = false };
        var result = _toggleIntegration.Validate(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // ── SendFriendRequestRequestValidator ─────────────────────────────────

    private readonly SendFriendRequestRequestValidator _sendFriendRequest = new();

    [Fact]
    public void Validate_SendFriendRequestValidEmail_ShouldPass()
    {
        _sendFriendRequest.Validate(new SendFriendRequestRequest("banbe@example.com"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SendFriendRequestWithConnectionId_ShouldPass()
    {
        var request = new SendFriendRequestRequest("banbe@example.com", Guid.NewGuid());
        _sendFriendRequest.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SendFriendRequestEmptyEmail_ShouldFail()
    {
        var result = _sendFriendRequest.Validate(new SendFriendRequestRequest(""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_SendFriendRequestMalformedEmail_ShouldFail()
    {
        var result = _sendFriendRequest.Validate(new SendFriendRequestRequest("banbe-example.com"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_SendFriendRequestEmailOver256Chars_ShouldFail()
    {
        var longEmail = new string('a', 250) + "@example.com"; // 262 ký tự
        var result = _sendFriendRequest.Validate(new SendFriendRequestRequest(longEmail));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }
}
