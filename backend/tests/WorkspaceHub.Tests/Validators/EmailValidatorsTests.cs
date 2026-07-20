using FluentAssertions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Application.Validators;

namespace WorkspaceHub.Tests.Validators;

/// <summary>
/// Unit test cho nhóm validator Email: SendEmail / ReplyEmail / ForwardEmail / SaveDraft /
/// AttachmentUpload / CreateScheduledEmail / SendEmailToGuests.
/// </summary>
public class EmailValidatorsTests
{
    /// <summary>Attachment hợp lệ nhỏ gọn, dùng chung cho nhiều case.</summary>
    private static AttachmentUpload SmallAttachment() => new()
    {
        Filename = "bao-cao.pdf",
        MimeType = "application/pdf",
        ContentBase64 = "SGVsbG8gV29ybGQ="
    };

    /// <summary>
    /// Bộ attachment vượt 25MB: dùng CHUNG 1 string 1 triệu ký tự cho 35 phần tử
    /// (chỉ tốn ~2MB RAM nhưng EstimateTotalBytes ≈ 26.25MB &gt; 25MB).
    /// </summary>
    private static List<AttachmentUpload> OversizedAttachments()
    {
        var chunk = new string('A', 1_000_000); // 1_000_000 / 4 * 3 = 750_000 bytes / file
        return Enumerable.Range(0, 35)
            .Select(i => new AttachmentUpload { Filename = $"file{i}.bin", ContentBase64 = chunk })
            .ToList();
    }

    // ── SendEmailRequestValidator ─────────────────────────────────────────

    private readonly SendEmailRequestValidator _sendEmail = new();

    private static SendEmailRequest ValidSendEmail() => new()
    {
        ConnectionId = Guid.NewGuid(),
        To = new List<string> { "nguoinhan@example.com" },
        Subject = "Báo cáo tuần",
        BodyHtml = "<p>Nội dung email</p>"
    };

    [Fact]
    public void Validate_SendEmailValidRequest_ShouldPass()
    {
        _sendEmail.Validate(ValidSendEmail()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SendEmailEmptyConnectionId_ShouldFail()
    {
        var request = ValidSendEmail();
        request.ConnectionId = Guid.Empty;

        var result = _sendEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConnectionId");
    }

    [Fact]
    public void Validate_SendEmailEmptyToList_ShouldFail()
    {
        var request = ValidSendEmail();
        request.To = new List<string>();

        var result = _sendEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "To");
    }

    [Fact]
    public void Validate_SendEmailMalformedRecipient_ShouldFail()
    {
        var request = ValidSendEmail();
        request.To = new List<string> { "khong-phai-email" };

        var result = _sendEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "To[0]");
    }

    [Fact]
    public void Validate_SendEmailMalformedCc_ShouldFail()
    {
        var request = ValidSendEmail();
        request.Cc = new List<string> { "sai-dinh-dang" };

        var result = _sendEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Cc[0]");
    }

    [Fact]
    public void Validate_SendEmailEmptySubject_ShouldFail()
    {
        var request = ValidSendEmail();
        request.Subject = "";

        var result = _sendEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Subject");
    }

    [Fact]
    public void Validate_SendEmailSubjectOver500Chars_ShouldFail()
    {
        var request = ValidSendEmail();
        request.Subject = new string('s', 501);

        var result = _sendEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Subject");
    }

    [Fact]
    public void Validate_SendEmailEmptyBodyHtml_ShouldFail()
    {
        var request = ValidSendEmail();
        request.BodyHtml = "";

        var result = _sendEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BodyHtml");
    }

    [Fact]
    public void Validate_SendEmailAttachmentsOver25Mb_ShouldFail()
    {
        var request = ValidSendEmail();
        request.Attachments = OversizedAttachments();

        var result = _sendEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Attachments");
    }

    [Fact]
    public void Validate_SendEmailWithSmallAttachment_ShouldPass()
    {
        var request = ValidSendEmail();
        request.Attachments = new List<AttachmentUpload> { SmallAttachment() };

        _sendEmail.Validate(request).IsValid.Should().BeTrue();
    }

    // ── ReplyEmailRequestValidator ────────────────────────────────────────

    private readonly ReplyEmailRequestValidator _replyEmail = new();

    private static ReplyEmailRequest ValidReplyEmail() => new()
    {
        ConnectionId = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        BodyHtml = "<p>Đã nhận, cảm ơn.</p>",
        ReplyAll = true
    };

    [Fact]
    public void Validate_ReplyEmailValidRequest_ShouldPass()
    {
        _replyEmail.Validate(ValidReplyEmail()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReplyEmailEmptyConnectionId_ShouldFail()
    {
        var request = ValidReplyEmail();
        request.ConnectionId = Guid.Empty;

        var result = _replyEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConnectionId");
    }

    [Fact]
    public void Validate_ReplyEmailEmptyItemId_ShouldFail()
    {
        var request = ValidReplyEmail();
        request.ItemId = Guid.Empty;

        var result = _replyEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ItemId");
    }

    [Fact]
    public void Validate_ReplyEmailEmptyBodyHtml_ShouldFail()
    {
        var request = ValidReplyEmail();
        request.BodyHtml = "";

        var result = _replyEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BodyHtml");
    }

    [Fact]
    public void Validate_ReplyEmailMalformedBcc_ShouldFail()
    {
        var request = ValidReplyEmail();
        request.Bcc = new List<string> { "bcc-sai" };

        var result = _replyEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Bcc[0]");
    }

    // ── ForwardEmailRequestValidator ──────────────────────────────────────

    private readonly ForwardEmailRequestValidator _forwardEmail = new();

    private static ForwardEmailRequest ValidForwardEmail() => new()
    {
        ConnectionId = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        To = new List<string> { "bandong@example.com" },
        BodyHtml = "<p>FYI</p>"
    };

    [Fact]
    public void Validate_ForwardEmailValidRequest_ShouldPass()
    {
        _forwardEmail.Validate(ValidForwardEmail()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ForwardEmailEmptyItemId_ShouldFail()
    {
        var request = ValidForwardEmail();
        request.ItemId = Guid.Empty;

        var result = _forwardEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ItemId");
    }

    [Fact]
    public void Validate_ForwardEmailEmptyToList_ShouldFail()
    {
        var request = ValidForwardEmail();
        request.To = new List<string>();

        var result = _forwardEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "To");
    }

    [Fact]
    public void Validate_ForwardEmailMalformedRecipient_ShouldFail()
    {
        var request = ValidForwardEmail();
        request.To = new List<string> { "nguoinhan(at)example.com" };

        var result = _forwardEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "To[0]");
    }

    [Fact]
    public void Validate_ForwardEmailEmptyBodyHtml_ShouldFail()
    {
        var request = ValidForwardEmail();
        request.BodyHtml = "";

        var result = _forwardEmail.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BodyHtml");
    }

    // ── SaveDraftRequestValidator ─────────────────────────────────────────

    private readonly SaveDraftRequestValidator _saveDraft = new();

    [Fact]
    public void Validate_SaveDraftOnlyConnectionId_ShouldPass()
    {
        // Draft cho phép trống subject/body/to — chỉ cần ConnectionId.
        var request = new SaveDraftRequest { ConnectionId = Guid.NewGuid() };
        _saveDraft.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SaveDraftEmptyConnectionId_ShouldFail()
    {
        var request = new SaveDraftRequest { ConnectionId = Guid.Empty };

        var result = _saveDraft.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConnectionId");
    }

    [Fact]
    public void Validate_SaveDraftMalformedTo_ShouldFail()
    {
        var request = new SaveDraftRequest
        {
            ConnectionId = Guid.NewGuid(),
            To = new List<string> { "khong-phai-email" }
        };

        var result = _saveDraft.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "To[0]");
    }

    [Fact]
    public void Validate_SaveDraftSubjectOver500Chars_ShouldFail()
    {
        var request = new SaveDraftRequest
        {
            ConnectionId = Guid.NewGuid(),
            Subject = new string('s', 501)
        };

        var result = _saveDraft.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Subject");
    }

    [Fact]
    public void Validate_SaveDraftAttachmentsOver25Mb_ShouldFail()
    {
        var request = new SaveDraftRequest
        {
            ConnectionId = Guid.NewGuid(),
            Attachments = OversizedAttachments()
        };

        var result = _saveDraft.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Attachments");
    }

    // ── AttachmentUploadValidator ─────────────────────────────────────────

    private readonly AttachmentUploadValidator _attachment = new();

    [Fact]
    public void Validate_AttachmentValidFile_ShouldPass()
    {
        _attachment.Validate(SmallAttachment()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AttachmentEmptyFilename_ShouldFail()
    {
        var attachment = SmallAttachment();
        attachment.Filename = "";

        var result = _attachment.Validate(attachment);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Filename");
    }

    [Fact]
    public void Validate_AttachmentFilenameOver255Chars_ShouldFail()
    {
        var attachment = SmallAttachment();
        attachment.Filename = new string('f', 256);

        var result = _attachment.Validate(attachment);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Filename");
    }

    [Fact]
    public void Validate_AttachmentEmptyContentBase64_ShouldFail()
    {
        var attachment = SmallAttachment();
        attachment.ContentBase64 = "";

        var result = _attachment.Validate(attachment);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ContentBase64");
    }

    // ── CreateScheduledEmailRequestValidator ──────────────────────────────

    private readonly CreateScheduledEmailRequestValidator _scheduled = new();

    private static CreateScheduledEmailRequest ValidScheduled() => new()
    {
        ConnectionId = Guid.NewGuid(),
        To = new List<string> { "nguoinhan@example.com" },
        Subject = "Nhắc họp",
        BodyHtml = "<p>9h sáng mai</p>",
        SendAt = DateTime.UtcNow.AddHours(2)
    };

    [Fact]
    public void Validate_ScheduledValidRequest_ShouldPass()
    {
        _scheduled.Validate(ValidScheduled()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ScheduledEmptyConnectionId_ShouldFail()
    {
        var request = ValidScheduled();
        request.ConnectionId = Guid.Empty;

        var result = _scheduled.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConnectionId");
    }

    [Fact]
    public void Validate_ScheduledEmptyToList_ShouldFail()
    {
        var request = ValidScheduled();
        request.To = new List<string>();

        var result = _scheduled.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "To");
    }

    [Fact]
    public void Validate_ScheduledSubjectOver500Chars_ShouldFail()
    {
        var request = ValidScheduled();
        request.Subject = new string('s', 501);

        var result = _scheduled.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Subject");
    }

    [Fact]
    public void Validate_ScheduledSendAtInThePast_ShouldFail()
    {
        var request = ValidScheduled();
        request.SendAt = DateTime.UtcNow.AddMinutes(-10);

        var result = _scheduled.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SendAt");
    }

    [Fact]
    public void Validate_ScheduledSendAtNotUtc_ShouldFail()
    {
        var request = ValidScheduled();
        // Kind = Unspecified → vi phạm rule "phải là UTC".
        request.SendAt = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(2), DateTimeKind.Unspecified);

        var result = _scheduled.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SendAt");
    }

    // ── SendEmailToGuestsRequestValidator ─────────────────────────────────

    private readonly SendEmailToGuestsRequestValidator _sendToGuests = new();

    private static SendEmailToGuestsRequest ValidSendToGuests() =>
        new(new List<string> { "khach@example.com" }, "Lịch họp", "<p>Xin chào</p>", false);

    [Fact]
    public void Validate_SendToGuestsValidRequest_ShouldPass()
    {
        _sendToGuests.Validate(ValidSendToGuests()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SendToGuestsEmptyRecipientList_ShouldFail()
    {
        var request = ValidSendToGuests() with { RecipientEmails = new List<string>() };

        var result = _sendToGuests.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RecipientEmails");
    }

    [Fact]
    public void Validate_SendToGuestsRecipientListContainsBlank_ShouldFail()
    {
        var request = ValidSendToGuests() with
        {
            RecipientEmails = new List<string> { "khach@example.com", "  " }
        };

        var result = _sendToGuests.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RecipientEmails");
    }

    [Fact]
    public void Validate_SendToGuestsEmptySubject_ShouldFail()
    {
        var request = ValidSendToGuests() with { Subject = "" };

        var result = _sendToGuests.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Subject");
    }

    [Fact]
    public void Validate_SendToGuestsBodyOver5000Chars_ShouldFail()
    {
        var request = ValidSendToGuests() with { BodyHtml = new string('b', 5001) };

        var result = _sendToGuests.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BodyHtml");
    }
}
