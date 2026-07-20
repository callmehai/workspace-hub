using FluentAssertions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Validators;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Tests.Validators;

/// <summary>
/// Unit test cho nhóm validator Item: CreateNote / CreateEvent / UpdateItemStatus / Rsvp.
/// </summary>
public class ItemValidatorsTests
{
    // ── CreateNoteValidator ───────────────────────────────────────────────

    private readonly CreateNoteValidator _createNote = new();

    private static CreateNoteRequest ValidNote() =>
        new("Ghi chú họp", "## Nội dung\n- Điểm 1");

    [Fact]
    public void Validate_CreateNoteValidRequest_ShouldPass()
    {
        _createNote.Validate(ValidNote()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CreateNoteEmptyTitle_ShouldFail()
    {
        var request = ValidNote() with { Title = "" };
        var result = _createNote.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Fact]
    public void Validate_CreateNoteTitleOver200Chars_ShouldFail()
    {
        var request = ValidNote() with { Title = new string('t', 201) };
        var result = _createNote.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Fact]
    public void Validate_CreateNoteEmptyContent_ShouldFail()
    {
        var request = ValidNote() with { ContentMarkdown = "" };
        var result = _createNote.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ContentMarkdown");
    }

    [Fact]
    public void Validate_CreateNoteContentOver50000Chars_ShouldFail()
    {
        var request = ValidNote() with { ContentMarkdown = new string('c', 50_001) };
        var result = _createNote.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ContentMarkdown");
    }

    [Fact]
    public void Validate_CreateNoteEmptyGuidFolderId_ShouldFail()
    {
        var request = ValidNote() with { FolderId = Guid.Empty };
        var result = _createNote.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FolderId");
    }

    [Fact]
    public void Validate_CreateNoteNullFolderId_ShouldPass()
    {
        // FolderId null = note không thuộc folder nào — rule chỉ chạy When(HasValue).
        var request = ValidNote() with { FolderId = null };
        _createNote.Validate(request).IsValid.Should().BeTrue();
    }

    // ── CreateEventRequestValidator ───────────────────────────────────────

    private readonly CreateEventRequestValidator _createEvent = new();

    private static CreateEventRequest ValidEvent() => new(
        Guid.NewGuid(),
        "Họp sprint review",
        new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Validate_CreateEventValidRequest_ShouldPass()
    {
        _createEvent.Validate(ValidEvent()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CreateEventEmptyConnectionId_ShouldFail()
    {
        var request = ValidEvent() with { ConnectionId = Guid.Empty };
        var result = _createEvent.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConnectionId");
    }

    [Fact]
    public void Validate_CreateEventEmptyTitle_ShouldFail()
    {
        var request = ValidEvent() with { Title = "" };
        var result = _createEvent.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Fact]
    public void Validate_CreateEventEndBeforeStart_ShouldFail()
    {
        var request = ValidEvent() with
        {
            End = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero)
        };
        var result = _createEvent.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "End");
    }

    [Fact]
    public void Validate_CreateEventEndEqualsStart_ShouldFail()
    {
        var request = ValidEvent() with { End = ValidEvent().Start };
        var result = _createEvent.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "End");
    }

    [Fact]
    public void Validate_CreateEventDefaultStart_ShouldFail()
    {
        var request = ValidEvent() with { Start = default };
        var result = _createEvent.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Start");
    }

    [Fact]
    public void Validate_CreateEventMalformedAttendee_ShouldFail()
    {
        var request = ValidEvent() with
        {
            Attendees = new List<string> { "khong-phai-email" }
        };
        var result = _createEvent.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Attendees[0]");
    }

    [Fact]
    public void Validate_CreateEventMoreThan200Attendees_ShouldFail()
    {
        var request = ValidEvent() with
        {
            Attendees = Enumerable.Range(0, 201).Select(i => $"guest{i}@example.com").ToList()
        };
        var result = _createEvent.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Attendees");
    }

    [Fact]
    public void Validate_CreateEventMoreThan20DriveFiles_ShouldFail()
    {
        var request = ValidEvent() with
        {
            DriveItemIds = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToList()
        };
        var result = _createEvent.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DriveItemIds");
    }

    [Fact]
    public void Validate_CreateEventValidReminder_ShouldPass()
    {
        var request = ValidEvent() with
        {
            Reminders = new List<EventReminderDto>
            {
                new(null, ReminderType.GooglePopup, 30, ReminderUnit.Minutes, null)
            }
        };
        _createEvent.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CreateEventNegativeReminderOffset_ShouldFail()
    {
        // Child validator EventReminderDtoValidator được gọi qua RuleForEach.SetValidator.
        var request = ValidEvent() with
        {
            Reminders = new List<EventReminderDto>
            {
                new(null, ReminderType.GooglePopup, -5, ReminderUnit.Minutes, null)
            }
        };
        var result = _createEvent.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.StartsWith("Reminders[0]"));
    }

    // ── UpdateItemStatusValidator ─────────────────────────────────────────

    private readonly UpdateItemStatusValidator _updateStatus = new();

    [Theory]
    [InlineData(ItemStatus.Inbox)]
    [InlineData(ItemStatus.Doing)]
    [InlineData(ItemStatus.Done)]
    public void Validate_UpdateItemStatusKnownStatus_ShouldPass(ItemStatus status)
    {
        _updateStatus.Validate(new UpdateItemStatusRequest(status)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UpdateItemStatusUndefinedEnumValue_ShouldFail()
    {
        var result = _updateStatus.Validate(new UpdateItemStatusRequest((ItemStatus)99));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Status");
    }

    [Fact]
    public void Validate_UpdateItemStatusNegativeEnumValue_ShouldFail()
    {
        var result = _updateStatus.Validate(new UpdateItemStatusRequest((ItemStatus)(-1)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Status");
    }

    // ── RsvpRequestValidator ──────────────────────────────────────────────

    private readonly RsvpRequestValidator _rsvp = new();

    [Theory]
    [InlineData("accepted")]
    [InlineData("declined")]
    [InlineData("tentative")]
    [InlineData("needsAction")]
    public void Validate_RsvpAllowedResponse_ShouldPass(string response)
    {
        _rsvp.Validate(new RsvpRequest(response, null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RsvpWithComment_ShouldPass()
    {
        _rsvp.Validate(new RsvpRequest("accepted", "Mình sẽ tới trễ 10 phút"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RsvpEmptyResponse_ShouldFail()
    {
        var result = _rsvp.Validate(new RsvpRequest("", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Response");
    }

    [Fact]
    public void Validate_RsvpUnknownResponse_ShouldFail()
    {
        var result = _rsvp.Validate(new RsvpRequest("maybe", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Response");
    }

    [Fact]
    public void Validate_RsvpWrongCasingResponse_ShouldFail()
    {
        // So sánh chuỗi là case-sensitive → "Accepted" không hợp lệ.
        var result = _rsvp.Validate(new RsvpRequest("Accepted", null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Response");
    }
}
