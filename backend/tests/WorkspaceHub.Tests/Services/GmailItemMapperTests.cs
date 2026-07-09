using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class GmailItemMapperTests
{
    private readonly GmailItemMapper _mapper;

    public GmailItemMapperTests()
    {
        _mapper = new GmailItemMapper();
    }

    [Fact]
    public void ToItem_WithValidSubject_SetsTitleAndBasicFields()
    {
        var msg = new GmailMessage("1", "t1", "My Subject", null, new List<string>(), new List<string>(), new List<string>(), null, new List<string>(), false, null);
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();

        var item = _mapper.ToItem(msg, userId, connId, new HashSet<string>());

        item.Title.Should().Be("My Subject");
        item.Type.Should().Be(ItemType.Email);
        item.Status.Should().Be(ItemStatus.Inbox);
        item.ExternalId.Should().Be("1");
        item.ConnectionId.Should().Be(connId);
        item.UserId.Should().Be(userId);
    }

    [Fact]
    public void ToItem_WithNullSubject_SetsDefaultTitle()
    {
        var msg = new GmailMessage("1", "t1", null, null, new List<string>(), new List<string>(), new List<string>(), null, new List<string>(), false, null);
        var item = _mapper.ToItem(msg, Guid.NewGuid(), Guid.NewGuid(), new HashSet<string>());
        item.Title.Should().Be("(Không có tiêu đề)");
    }

    [Fact]
    public void ToItem_FromInImportantEmails_SetsIsImportantTrue()
    {
        var msg = new GmailMessage("1", "t1", "Sub", "Boss <boss@x.com>", new List<string>(), new List<string>(), new List<string>(), null, new List<string>(), false, null);
        var item = _mapper.ToItem(msg, Guid.NewGuid(), Guid.NewGuid(), new HashSet<string> { "boss@x.com" });
        item.IsImportant.Should().BeTrue();
    }

    [Fact]
    public void ToItem_FromNotInImportantEmails_SetsIsImportantFalse()
    {
        var msg = new GmailMessage("1", "t1", "Sub", "Boss <boss@x.com>", new List<string>(), new List<string>(), new List<string>(), null, new List<string>(), false, null);
        var item = _mapper.ToItem(msg, Guid.NewGuid(), Guid.NewGuid(), new HashSet<string> { "other@x.com" });
        item.IsImportant.Should().BeFalse();
    }

    [Fact]
    public void ToItem_InvalidFrom_DoesNotThrowAndSetsIsImportantFalse()
    {
        var msg = new GmailMessage("1", "t1", "Sub", "invalid_email_format", new List<string>(), new List<string>(), new List<string>(), null, new List<string>(), false, null);
        var item = _mapper.ToItem(msg, Guid.NewGuid(), Guid.NewGuid(), new HashSet<string> { "invalid_email_format" });
        item.IsImportant.Should().BeFalse();
    }

    [Fact]
    public void ToItem_SerializesMetadataJsonCorrectly()
    {
        var msg = new GmailMessage("123", "t1", "Sub", "A <a@b.com>", new List<string> { "c@d.com" }, new List<string>(), new List<string>(), null, new List<string> { "INBOX" }, true, null);
        var item = _mapper.ToItem(msg, Guid.NewGuid(), Guid.NewGuid(), new HashSet<string>());

        var metadata = JsonDocument.Parse(item.MetadataJson).RootElement;
        metadata.GetProperty("from").GetString().Should().Be("A <a@b.com>");
        metadata.GetProperty("to")[0].GetString().Should().Be("c@d.com");
        metadata.GetProperty("threadId").GetString().Should().Be("t1");
        metadata.GetProperty("labels")[0].GetString().Should().Be("INBOX");
        metadata.GetProperty("hasAttachment").GetBoolean().Should().BeTrue();
        metadata.GetProperty("webUrl").GetString().Should().Be("https://mail.google.com/mail/u/0/#all/123");
    }

    [Fact]
    public void ToItem_WithOccurredAt_SetsUtcDateTime()
    {
        var dtoOffset = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.FromHours(7));
        var msg = new GmailMessage("1", "t1", "S", null, new List<string>(), new List<string>(), new List<string>(), null, new List<string>(), false, dtoOffset);
        
        var item = _mapper.ToItem(msg, Guid.NewGuid(), Guid.NewGuid(), new HashSet<string>());
        
        item.OccurredAt.Should().Be(dtoOffset.UtcDateTime);
        item.OccurredAt.Kind.Should().Be(DateTimeKind.Utc);
    }
}
