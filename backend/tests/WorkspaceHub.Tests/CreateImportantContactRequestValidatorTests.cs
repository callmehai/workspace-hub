using FluentAssertions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Validators;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests;

public class CreateImportantContactRequestValidatorTests
{
    private readonly CreateImportantContactRequestValidator _validator = new();

    [Fact]
    public void ValidEmail_Passes()
    {
        var req = new CreateImportantContactRequest(ImportantContactType.Email, "boss@company.com", "Boss");
        _validator.Validate(req).IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmailType_InvalidEmail_Fails()
    {
        var req = new CreateImportantContactRequest(ImportantContactType.Email, "not-an-email", "Boss");
        _validator.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void JiraAccount_FreeFormIdentifier_Passes()
    {
        // accountId không phải email → vẫn hợp lệ cho JiraAccount.
        var req = new CreateImportantContactRequest(ImportantContactType.JiraAccount, "5b10ac8d82e05b22cc7d4ef5", "Lead Dev");
        _validator.Validate(req).IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmptyIdentifier_Fails()
    {
        var req = new CreateImportantContactRequest(ImportantContactType.JiraAccount, "", "Boss");
        _validator.Validate(req).IsValid.Should().BeFalse();
    }

    [Fact]
    public void EmptyLabel_Fails()
    {
        var req = new CreateImportantContactRequest(ImportantContactType.JiraAccount, "acc-1", "");
        _validator.Validate(req).IsValid.Should().BeFalse();
    }
}
