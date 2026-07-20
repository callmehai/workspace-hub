using FluentAssertions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Validators;

namespace WorkspaceHub.Tests.Validators;

/// <summary>
/// Unit test cho nhóm validator Folder: CreateFolder / UpdateFolder / AddItemToFolder /
/// InviteFolderShare / UpdateFolderShare.
/// </summary>
public class FolderValidatorsTests
{
    // ── CreateFolderValidator ─────────────────────────────────────────────

    private readonly CreateFolderValidator _createFolder = new();

    private static CreateFolderRequest ValidCreateFolder() =>
        new("Dự án PRN232", "#4F46E5", "folder");

    [Fact]
    public void Validate_CreateFolderValidRequest_ShouldPass()
    {
        _createFolder.Validate(ValidCreateFolder()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CreateFolderEmptyName_ShouldFail()
    {
        var request = ValidCreateFolder() with { Name = "" };
        var result = _createFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_CreateFolderNameOver200Chars_ShouldFail()
    {
        var request = ValidCreateFolder() with { Name = new string('a', 201) };
        var result = _createFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_CreateFolderInvalidHexColor_ShouldFail()
    {
        var request = ValidCreateFolder() with { Color = "blue" };
        var result = _createFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Color");
    }

    [Fact]
    public void Validate_CreateFolderShortHexColor_ShouldPass()
    {
        // Regex chấp nhận cả #RGB lẫn #RRGGBB.
        var request = ValidCreateFolder() with { Color = "#ABC" };
        _createFolder.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CreateFolderEmptyIcon_ShouldFail()
    {
        var request = ValidCreateFolder() with { Icon = "" };
        var result = _createFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Icon");
    }

    [Fact]
    public void Validate_CreateFolderIconOver100Chars_ShouldFail()
    {
        var request = ValidCreateFolder() with { Icon = new string('i', 101) };
        var result = _createFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Icon");
    }

    // ── UpdateFolderValidator ─────────────────────────────────────────────

    private readonly UpdateFolderValidator _updateFolder = new();

    private static UpdateFolderRequest ValidUpdateFolder() =>
        new("Dự án PRN232", "#4F46E5", "folder", 0);

    [Fact]
    public void Validate_UpdateFolderValidRequest_ShouldPass()
    {
        _updateFolder.Validate(ValidUpdateFolder()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UpdateFolderEmptyName_ShouldFail()
    {
        var request = ValidUpdateFolder() with { Name = "" };
        var result = _updateFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_UpdateFolderInvalidHexColor_ShouldFail()
    {
        var request = ValidUpdateFolder() with { Color = "#12345" };
        var result = _updateFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Color");
    }

    [Fact]
    public void Validate_UpdateFolderNegativeSortOrder_ShouldFail()
    {
        var request = ValidUpdateFolder() with { SortOrder = -1 };
        var result = _updateFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SortOrder");
    }

    [Fact]
    public void Validate_UpdateFolderZeroSortOrder_ShouldPass()
    {
        var request = ValidUpdateFolder() with { SortOrder = 0 };
        _updateFolder.Validate(request).IsValid.Should().BeTrue();
    }

    // ── AddItemToFolderValidator ──────────────────────────────────────────

    private readonly AddItemToFolderValidator _addItemToFolder = new();

    [Fact]
    public void Validate_AddItemToFolderValidItemId_ShouldPass()
    {
        var result = _addItemToFolder.Validate(new AddItemToFolderRequest(Guid.NewGuid()));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AddItemToFolderEmptyItemId_ShouldFail()
    {
        var result = _addItemToFolder.Validate(new AddItemToFolderRequest(Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ItemId");
    }

    // ── InviteFolderShareRequestValidator ─────────────────────────────────

    private readonly InviteFolderShareRequestValidator _inviteShare = new();

    [Fact]
    public void Validate_InviteShareValidRequest_ShouldPass()
    {
        var result = _inviteShare.Validate(new InviteFolderShareRequest(Guid.NewGuid(), "Viewer"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_InviteShareLowercasePermission_ShouldPass()
    {
        // Enum.TryParse dùng ignoreCase: true.
        var result = _inviteShare.Validate(new InviteFolderShareRequest(Guid.NewGuid(), "editor"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_InviteShareEmptyFriendUserId_ShouldFail()
    {
        var result = _inviteShare.Validate(new InviteFolderShareRequest(Guid.Empty, "Viewer"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FriendUserId");
    }

    [Fact]
    public void Validate_InviteShareEmptyPermission_ShouldFail()
    {
        var result = _inviteShare.Validate(new InviteFolderShareRequest(Guid.NewGuid(), ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Permission");
    }

    [Fact]
    public void Validate_InviteShareUnknownPermission_ShouldFail()
    {
        var result = _inviteShare.Validate(new InviteFolderShareRequest(Guid.NewGuid(), "Owner"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Permission");
    }

    // ── UpdateFolderShareRequestValidator ─────────────────────────────────

    private readonly UpdateFolderShareRequestValidator _updateShare = new();

    [Fact]
    public void Validate_UpdateShareValidPermission_ShouldPass()
    {
        _updateShare.Validate(new UpdateFolderShareRequest("Editor")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UpdateShareEmptyPermission_ShouldFail()
    {
        var result = _updateShare.Validate(new UpdateFolderShareRequest(""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Permission");
    }

    [Fact]
    public void Validate_UpdateShareUnknownPermission_ShouldFail()
    {
        var result = _updateShare.Validate(new UpdateFolderShareRequest("SuperAdmin"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Permission");
    }
}
