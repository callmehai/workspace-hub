using System;
using System.Text.Json;
using FluentAssertions;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class JiraItemMapperTests
{
    private readonly JiraItemMapper _mapper = new();

    private static JsonElement Adf(string text) => JsonDocument.Parse($$"""
        { "type": "doc", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "{{text}}" } ] } ] }
        """).RootElement;

    private static JiraIssue SampleIssue(
        string id = "10001",
        string key = "SCRUM-1",
        string? summary = "Fix the bug",
        JsonElement? description = null,
        DateTimeOffset? updated = null,
        string? statusCategoryKey = "indeterminate",
        string? statusName = "In Progress") =>
        new(
            id, key, "SCRUM", "Scrum Project", summary, description,
            statusName, "Loc Hoang", "account123", "High", "Task",
            "https://api.atlassian.com/ex/jira/cloud-1/browse/SCRUM-1",
            updated,
            statusCategoryKey);

    [Fact]
    public void ToItem_SetsTypeTicketAndBasicFields()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();

        var item = _mapper.ToItem(SampleIssue(), userId, connId);

        item.Type.Should().Be(ItemType.Ticket);
        item.Status.Should().Be(ItemStatus.Doing);
        item.Title.Should().Be("Fix the bug");
        item.ExternalId.Should().Be("10001");
        item.ConnectionId.Should().Be(connId);
        item.UserId.Should().Be(userId);
    }

    [Theory]
    [InlineData("new", ItemStatus.Inbox)]
    [InlineData("indeterminate", ItemStatus.Doing)]
    [InlineData("done", ItemStatus.Done)]
    [InlineData("unknown_key", ItemStatus.Inbox)]
    public void ToItem_MapsStatusUsingCategoryKey(string categoryKey, ItemStatus expectedStatus)
    {
        var item = _mapper.ToItem(SampleIssue(statusCategoryKey: categoryKey, statusName: ""), Guid.NewGuid(), Guid.NewGuid());
        item.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public void ToItem_PrioritizesCategoryKeyOverStatusName()
    {
        // Category key says "done" but status name contains "Progress" (Doing)
        var item = _mapper.ToItem(SampleIssue(statusCategoryKey: "done", statusName: "In Progress"), Guid.NewGuid(), Guid.NewGuid());
        item.Status.Should().Be(ItemStatus.Done);
    }

    [Theory]
    [InlineData("To Do", ItemStatus.Inbox)]
    [InlineData("In Progress", ItemStatus.Doing)]
    [InlineData("Done", ItemStatus.Done)]
    public void ToItem_FallbackToStatusName_WhenCategoryKeyIsNull(string statusName, ItemStatus expectedStatus)
    {
        var item = _mapper.ToItem(SampleIssue(statusCategoryKey: null, statusName: statusName), Guid.NewGuid(), Guid.NewGuid());
        item.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public void ToItem_NullSummary_SetsDefaultTitle()
    {
        var item = _mapper.ToItem(SampleIssue(summary: null), Guid.NewGuid(), Guid.NewGuid());
        item.Title.Should().Be("(Không có tiêu đề)");
    }

    [Fact]
    public void ToItem_DescriptionAdf_ConvertedToSnippet()
    {
        var item = _mapper.ToItem(SampleIssue(description: Adf("a detailed description")), Guid.NewGuid(), Guid.NewGuid());
        item.Snippet.Should().Be("a detailed description");
    }

    [Fact]
    public void ToItem_NullDescription_EmptySnippet()
    {
        var item = _mapper.ToItem(SampleIssue(description: null), Guid.NewGuid(), Guid.NewGuid());
        item.Snippet.Should().BeEmpty();
    }

    [Fact]
    public void ToItem_LongDescription_TruncatesSnippetTo200()
    {
        var longText = new string('x', 500);
        var item = _mapper.ToItem(SampleIssue(description: Adf(longText)), Guid.NewGuid(), Guid.NewGuid());
        item.Snippet.Length.Should().Be(200);
    }

    [Fact]
    public void ToItem_UpdatedSetsETagAndOccurredAt()
    {
        var updated = new DateTimeOffset(2026, 6, 28, 10, 0, 0, TimeSpan.FromHours(7));
        var item = _mapper.ToItem(SampleIssue(updated: updated), Guid.NewGuid(), Guid.NewGuid());

        item.OccurredAt.Should().Be(updated.UtcDateTime);
        item.OccurredAt.Kind.Should().Be(DateTimeKind.Utc);
        // ETag dùng làm version-token cho conflict (SCRUM-57) — derive từ updated.
        item.ETag.Should().Be(updated.UtcDateTime.ToString("O"));
    }

    [Fact]
    public void ToItem_NullUpdated_ETagNullOccurredAtNow()
    {
        var before = DateTime.UtcNow;
        var item = _mapper.ToItem(SampleIssue(updated: null), Guid.NewGuid(), Guid.NewGuid());

        item.ETag.Should().BeNull();
        item.OccurredAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void ToItem_SerializesMetadataJson()
    {
        var item = _mapper.ToItem(SampleIssue(), Guid.NewGuid(), Guid.NewGuid());

        var md = JsonDocument.Parse(item.MetadataJson).RootElement;
        md.GetProperty("issueKey").GetString().Should().Be("SCRUM-1");
        md.GetProperty("projectKey").GetString().Should().Be("SCRUM");
        md.GetProperty("status").GetString().Should().Be("In Progress");
        md.GetProperty("assignee").GetString().Should().Be("Loc Hoang");
        md.GetProperty("priority").GetString().Should().Be("High");
        md.GetProperty("issueType").GetString().Should().Be("Task");
        md.GetProperty("issueUrl").GetString().Should().Be("https://api.atlassian.com/ex/jira/cloud-1/browse/SCRUM-1");
    }
}
