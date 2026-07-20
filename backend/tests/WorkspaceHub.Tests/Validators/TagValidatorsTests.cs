using FluentAssertions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Validators;

namespace WorkspaceHub.Tests.Validators;

/// <summary>
/// Unit test cho nhóm validator Tag (SCRUM-70): CreateTag / UpdateTag / AssignTag.
/// </summary>
public class TagValidatorsTests
{
    // ── CreateTagRequestValidator ─────────────────────────────────────────

    private readonly CreateTagRequestValidator _createTag = new();

    [Fact]
    public void Validate_CreateTagValidRequest_ShouldPass()
    {
        var result = _createTag.Validate(new CreateTagRequest("Quan trọng", "#4F46E5"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CreateTagEmptyName_ShouldFail()
    {
        var result = _createTag.Validate(new CreateTagRequest("", "#4F46E5"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_CreateTagNameOver100Chars_ShouldFail()
    {
        var result = _createTag.Validate(new CreateTagRequest(new string('a', 101), "#4F46E5"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_CreateTagEmptyColor_ShouldFail()
    {
        var result = _createTag.Validate(new CreateTagRequest("Quan trọng", ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Color");
    }

    [Fact]
    public void Validate_CreateTagInvalidHexColor_ShouldFail()
    {
        var result = _createTag.Validate(new CreateTagRequest("Quan trọng", "4F46E5"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Color");
    }

    [Fact]
    public void Validate_CreateTagShortHexColor_ShouldPass()
    {
        // Regex chấp nhận #RGB (3 ký tự) lẫn #RRGGBB (6 ký tự).
        _createTag.Validate(new CreateTagRequest("Tag", "#ABC")).IsValid.Should().BeTrue();
    }

    // ── UpdateTagRequestValidator ─────────────────────────────────────────

    private readonly UpdateTagRequestValidator _updateTag = new();

    [Fact]
    public void Validate_UpdateTagValidRequest_ShouldPass()
    {
        _updateTag.Validate(new UpdateTagRequest("Đã xử lý", "#00ff00")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UpdateTagEmptyName_ShouldFail()
    {
        var result = _updateTag.Validate(new UpdateTagRequest("  ", "#00ff00"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_UpdateTagNameOver100Chars_ShouldFail()
    {
        var result = _updateTag.Validate(new UpdateTagRequest(new string('b', 101), "#00ff00"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_UpdateTagInvalidHexColor_ShouldFail()
    {
        var result = _updateTag.Validate(new UpdateTagRequest("Đã xử lý", "#GGGGGG"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Color");
    }

    // ── AssignTagRequestValidator ─────────────────────────────────────────

    private readonly AssignTagRequestValidator _assignTag = new();

    [Fact]
    public void Validate_AssignTagValidItemId_ShouldPass()
    {
        _assignTag.Validate(new AssignTagRequest(Guid.NewGuid())).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AssignTagEmptyItemId_ShouldFail()
    {
        var result = _assignTag.Validate(new AssignTagRequest(Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ItemId");
    }
}
