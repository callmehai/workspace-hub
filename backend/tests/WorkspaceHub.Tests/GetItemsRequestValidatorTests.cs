using FluentValidation.TestHelper;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Validators;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Tests;

/// <summary>
/// Unit tests cho GetItemsRequestValidator — đảm bảo FluentValidation rules đúng.
/// </summary>
public class GetItemsRequestValidatorTests
{
    private readonly GetItemsRequestValidator _sut = new();

    // ───────────── Page validation ─────────────

    [Fact]
    public async Task Page_Zero_ShouldHaveError()
    {
        var request = new GetItemsRequest(Page: 0);
        var result = await _sut.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Fact]
    public async Task Page_Negative_ShouldHaveError()
    {
        var request = new GetItemsRequest(Page: -1);
        var result = await _sut.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Fact]
    public async Task Page_One_ShouldNotHaveError()
    {
        var request = new GetItemsRequest(Page: 1);
        var result = await _sut.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Page);
    }

    // ───────────── Limit validation ─────────────

    [Fact]
    public async Task Limit_Zero_ShouldHaveError()
    {
        var request = new GetItemsRequest(Limit: 0);
        var result = await _sut.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Limit);
    }

    [Fact]
    public async Task Limit_101_ShouldHaveError()
    {
        var request = new GetItemsRequest(Limit: 101);
        var result = await _sut.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Limit);
    }

    [Fact]
    public async Task Limit_100_ShouldNotHaveError()
    {
        var request = new GetItemsRequest(Limit: 100);
        var result = await _sut.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Limit);
    }

    [Fact]
    public async Task Limit_1_ShouldNotHaveError()
    {
        var request = new GetItemsRequest(Limit: 1);
        var result = await _sut.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Limit);
    }

    // ───────────── Search validation ─────────────

    [Fact]
    public async Task Search_Null_ShouldNotHaveError()
    {
        var request = new GetItemsRequest(Search: null);
        var result = await _sut.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Search);
    }

    [Fact]
    public async Task Search_Empty_ShouldNotHaveError()
    {
        var request = new GetItemsRequest(Search: "");
        var result = await _sut.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Search);
    }

    [Fact]
    public async Task Search_200Chars_ShouldNotHaveError()
    {
        var request = new GetItemsRequest(Search: new string('a', 200));
        var result = await _sut.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Search);
    }

    [Fact]
    public async Task Search_201Chars_ShouldHaveError()
    {
        var request = new GetItemsRequest(Search: new string('a', 201));
        var result = await _sut.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Search);
    }

    [Fact]
    public async Task Search_PaddedValid_ShouldNotHaveError()
    {
        var request = new GetItemsRequest(Search: "  " + new string('a', 200) + "  ");
        var result = await _sut.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Search);
    }

    // ───────────── Default values ─────────────

    [Fact]
    public async Task DefaultRequest_ShouldBeValid()
    {
        var request = new GetItemsRequest();
        var result = await _sut.TestValidateAsync(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task FullValidRequest_ShouldBeValid()
    {
        var request = new GetItemsRequest(
            FolderId: Guid.NewGuid(),
            Status: ItemStatus.Inbox,
            Type: ItemType.Email,
            IsImportant: true,
            Search: "meeting notes",
            Page: 3,
            Limit: 50);
        var result = await _sut.TestValidateAsync(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
