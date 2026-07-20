using FluentAssertions;
using WorkspaceHub.Application.DTOs.Drive;
using WorkspaceHub.Application.Validators;

namespace WorkspaceHub.Tests.Validators;

/// <summary>
/// Unit test cho nhóm validator Drive sharing: AddDrivePermission / UpdateDrivePermission /
/// CreateDriveFolder / LinkSharing. Role hợp lệ = reader | commenter | writer (owner KHÔNG hợp lệ).
/// </summary>
public class DriveValidatorsTests
{
    // ── AddDrivePermissionRequestValidator ────────────────────────────────

    private readonly AddDrivePermissionRequestValidator _addPermission = new();

    private static AddDrivePermissionRequest ValidAddPermission() =>
        new("banbe@example.com", "reader");

    [Fact]
    public void Validate_AddPermissionValidRequest_ShouldPass()
    {
        _addPermission.Validate(ValidAddPermission()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AddPermissionEmptyEmail_ShouldFail()
    {
        var request = ValidAddPermission() with { Email = "" };
        var result = _addPermission.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_AddPermissionMalformedEmail_ShouldFail()
    {
        var request = ValidAddPermission() with { Email = "banbe.example.com" };
        var result = _addPermission.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_AddPermissionEmptyRole_ShouldFail()
    {
        var request = ValidAddPermission() with { Role = "" };
        var result = _addPermission.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public void Validate_AddPermissionOwnerRole_ShouldFail()
    {
        // owner không assign được qua API app → DrivePermissionRoles.TryParse trả false.
        var request = ValidAddPermission() with { Role = "owner" };
        var result = _addPermission.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public void Validate_AddPermissionWriterRole_ShouldPass()
    {
        var request = ValidAddPermission() with { Role = "writer", Notify = false };
        _addPermission.Validate(request).IsValid.Should().BeTrue();
    }

    // ── UpdateDrivePermissionRequestValidator ─────────────────────────────

    private readonly UpdateDrivePermissionRequestValidator _updatePermission = new();

    [Fact]
    public void Validate_UpdatePermissionCommenterRole_ShouldPass()
    {
        _updatePermission.Validate(new UpdateDrivePermissionRequest("commenter"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UpdatePermissionEmptyRole_ShouldFail()
    {
        var result = _updatePermission.Validate(new UpdateDrivePermissionRequest(""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public void Validate_UpdatePermissionUnknownRole_ShouldFail()
    {
        var result = _updatePermission.Validate(new UpdateDrivePermissionRequest("admin"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    // ── CreateDriveFolderRequestValidator ─────────────────────────────────

    private readonly CreateDriveFolderRequestValidator _createDriveFolder = new();

    private static CreateDriveFolderRequest ValidCreateDriveFolder() =>
        new(Guid.NewGuid(), "Tài liệu dự án");

    [Fact]
    public void Validate_CreateDriveFolderValidRequest_ShouldPass()
    {
        _createDriveFolder.Validate(ValidCreateDriveFolder()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CreateDriveFolderEmptyConnectionId_ShouldFail()
    {
        var request = ValidCreateDriveFolder() with { ConnectionId = Guid.Empty };
        var result = _createDriveFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConnectionId");
    }

    [Fact]
    public void Validate_CreateDriveFolderEmptyName_ShouldFail()
    {
        var request = ValidCreateDriveFolder() with { Name = "" };
        var result = _createDriveFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_CreateDriveFolderNameOver255Chars_ShouldFail()
    {
        var request = ValidCreateDriveFolder() with { Name = new string('n', 256) };
        var result = _createDriveFolder.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_CreateDriveFolderWithParentItemId_ShouldPass()
    {
        var request = ValidCreateDriveFolder() with { ParentItemId = Guid.NewGuid() };
        _createDriveFolder.Validate(request).IsValid.Should().BeTrue();
    }

    // ── LinkSharingRequestValidator ───────────────────────────────────────

    private readonly LinkSharingRequestValidator _linkSharing = new();

    [Fact]
    public void Validate_LinkSharingEnabledWithRole_ShouldPass()
    {
        _linkSharing.Validate(new LinkSharingRequest(true, "reader")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_LinkSharingDisabledWithoutRole_ShouldPass()
    {
        // Tắt link không cần Role — rule chỉ áp dụng When(Enabled).
        _linkSharing.Validate(new LinkSharingRequest(false)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_LinkSharingEnabledWithoutRole_ShouldFail()
    {
        var result = _linkSharing.Validate(new LinkSharingRequest(true));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public void Validate_LinkSharingEnabledWithUnknownRole_ShouldFail()
    {
        var result = _linkSharing.Validate(new LinkSharingRequest(true, "owner"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public void Validate_LinkSharingDisabledWithConfirmRestrictParent_ShouldPass()
    {
        _linkSharing.Validate(new LinkSharingRequest(false, null, true))
            .IsValid.Should().BeTrue();
    }
}
