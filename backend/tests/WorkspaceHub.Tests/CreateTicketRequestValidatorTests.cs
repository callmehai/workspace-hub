using System;
using System.Collections.Generic;
using FluentAssertions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Validators;
using Xunit;

namespace WorkspaceHub.Tests;

public class CreateTicketRequestValidatorTests
{
    private readonly CreateTicketRequestValidator _validator = new();

    private static CreateTicketRequest Valid() =>
        new(Guid.NewGuid(), "SCRUM", "Task", "Fix the bug");

    [Fact]
    public void Valid_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmptyConnectionId_Fails()
    {
        var req = Valid() with { ConnectionId = Guid.Empty };
        _validator.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void EmptyProjectKey_Fails()
    {
        var req = Valid() with { ProjectKey = "" };
        _validator.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void EmptyIssueType_Fails()
    {
        var req = Valid() with { IssueType = "" };
        _validator.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void EmptySummary_Fails()
    {
        var req = Valid() with { Summary = "" };
        _validator.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void SummaryTooLong_Fails()
    {
        var req = Valid() with { Summary = new string('x', 256) };
        _validator.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void LabelWithWhitespace_Fails()
    {
        var req = Valid() with { Labels = new List<string> { "good", "has space" } };
        _validator.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidLabels_Pass()
    {
        var req = Valid() with { Labels = new List<string> { "backend", "urgent" } };
        _validator.Validate(req).IsValid.Should().BeTrue();
    }
}
