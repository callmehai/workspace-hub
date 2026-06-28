using System.Collections.Generic;
using FluentAssertions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Validators;
using Xunit;

namespace WorkspaceHub.Tests;

public class PatchItemRequestValidatorTests
{
    private readonly PatchItemRequestValidator _validator = new();

    [Fact]
    public void Empty_Fails()
    {
        _validator.Validate(new PatchItemRequest()).IsValid.Should().BeFalse();
    }

    [Fact]
    public void JiraSummaryOnly_Passes()
    {
        _validator.Validate(new PatchItemRequest(Summary: "new summary")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void JiraCommentOnly_Passes()
    {
        _validator.Validate(new PatchItemRequest(Comment: "a comment")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void JiraStatusTransitionOnly_Passes()
    {
        _validator.Validate(new PatchItemRequest(StatusTransition: "Done")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmptySummary_Fails()
    {
        _validator.Validate(new PatchItemRequest(Summary: "")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void SummaryTooLong_Fails()
    {
        _validator.Validate(new PatchItemRequest(Summary: new string('x', 256))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void LabelWithWhitespace_Fails()
    {
        _validator.Validate(new PatchItemRequest(Labels: new List<string> { "has space" })).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidLabels_Pass()
    {
        _validator.Validate(new PatchItemRequest(Labels: new List<string> { "backend" })).IsValid.Should().BeTrue();
    }
}
