using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

public class GmailGateway : IGmailGateway
{
    private readonly ITokenService _tokenService;

    public GmailGateway(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    private async Task<GmailService> BuildGmailServiceAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WorkspaceHub"
        });
    }

    public async Task<GmailProfile> GetProfileAsync(Connection connection, CancellationToken ct = default)
    {
        using var gmail = await BuildGmailServiceAsync(connection, ct);
        var profile = await gmail.Users.GetProfile("me").ExecuteAsync(ct);

        return new GmailProfile(
            profile.EmailAddress,
            profile.HistoryId,
            profile.MessagesTotal,
            profile.ThreadsTotal);
    }

    public async Task<GmailMessageList> ListMessageIdsAsync(Connection connection, string? pageToken, int maxResults, CancellationToken ct = default)
    {
        using var gmail = await BuildGmailServiceAsync(connection, ct);
        var request = gmail.Users.Messages.List("me");
        request.MaxResults = maxResults;
        if (!string.IsNullOrEmpty(pageToken))
        {
            request.PageToken = pageToken;
        }

        var response = await request.ExecuteAsync(ct);
        var messageIds = response.Messages?.Select(m => m.Id).ToList() ?? new List<string>();

        return new GmailMessageList(messageIds, response.NextPageToken);
    }

    public async Task<GmailMessage> GetMessageAsync(Connection connection, string messageId, CancellationToken ct = default)
    {
        using var gmail = await BuildGmailServiceAsync(connection, ct);
        var request = gmail.Users.Messages.Get("me", messageId);
        
        // Giải thích Format: 
        // FormatEnum.Metadata CÓ trả về headers (payload.headers), nhưng KHÔNG trả về danh sách các phần tử (payload.parts).
        // Yêu cầu "HasAttachment = true nếu có bất kỳ Payload.Parts nào có Filename khác rỗng" bắt buộc phải quét qua Parts.
        // Do đó, dùng FormatEnum.Full sẽ lấy đầy đủ cấu trúc để ta có thể kiểm tra attachment chính xác.
        request.Format = Google.Apis.Gmail.v1.UsersResource.MessagesResource.GetRequest.FormatEnum.Full;

        var msg = await request.ExecuteAsync(ct);

        var headers = msg.Payload?.Headers;
        var subject = headers?.FirstOrDefault(h => h.Name.Equals("Subject", StringComparison.OrdinalIgnoreCase))?.Value;
        var from = headers?.FirstOrDefault(h => h.Name.Equals("From", StringComparison.OrdinalIgnoreCase))?.Value;
        var toHeader = headers?.FirstOrDefault(h => h.Name.Equals("To", StringComparison.OrdinalIgnoreCase))?.Value;
        
        var toList = string.IsNullOrEmpty(toHeader) 
            ? (IReadOnlyList<string>)new List<string>() 
            : toHeader.Split(',').Select(x => x.Trim()).ToList();

        // Đệ quy tìm xem có Part nào chứa attachment (có filename)
        bool CheckHasAttachment(Google.Apis.Gmail.v1.Data.MessagePart part)
        {
            if (!string.IsNullOrEmpty(part.Filename)) return true;
            if (part.Parts != null)
            {
                foreach (var child in part.Parts)
                {
                    if (CheckHasAttachment(child)) return true;
                }
            }
            return false;
        }

        bool hasAttachment = msg.Payload != null && CheckHasAttachment(msg.Payload);

        DateTimeOffset? occurredAt = null;
        if (msg.InternalDate.HasValue)
        {
            occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(msg.InternalDate.Value);
        }

        return new GmailMessage(
            msg.Id,
            msg.ThreadId,
            subject,
            from,
            toList,
            msg.Snippet,
            msg.LabelIds?.ToList() ?? new List<string>(),
            hasAttachment,
            occurredAt,
            msg.HistoryId?.ToString());
    }

    public async Task<GmailHistory> ListHistoryAsync(Connection connection, string startHistoryId, string? pageToken, CancellationToken ct = default)
    {
        if (!ulong.TryParse(startHistoryId, out ulong historyId))
        {
            return new GmailHistory(true, new List<string>(), null, null);
        }

        using var gmail = await BuildGmailServiceAsync(connection, ct);
        var request = gmail.Users.History.List("me");
        request.StartHistoryId = historyId;
        if (!string.IsNullOrEmpty(pageToken))
        {
            request.PageToken = pageToken;
        }

        try
        {
            var response = await request.ExecuteAsync(ct);
            var addedIds = response.History?
                .Where(h => h.MessagesAdded != null)
                .SelectMany(h => h.MessagesAdded)
                .Where(m => m.Message?.Id != null)
                .Select(m => m.Message.Id)
                .Distinct()
                .ToList() ?? new List<string>();

            return new GmailHistory(
                false,
                addedIds,
                response.NextPageToken,
                response.HistoryId?.ToString());
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new GmailHistory(true, new List<string>(), null, null);
        }
    }

    public async Task ModifyMessageAsync(Connection connection, string messageId, IList<string> addLabelIds, IList<string> removeLabelIds, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            var req = new Google.Apis.Gmail.v1.Data.ModifyMessageRequest
            {
                AddLabelIds = addLabelIds,
                RemoveLabelIds = removeLabelIds
            };
            await gmail.Users.Messages.Modify(req, "me", messageId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new WorkspaceHub.Application.Common.ForbiddenException("Cần reconnect với quyền ghi.");
            }
            throw new WorkspaceHub.Application.Common.ProviderException($"Gmail API error: {ex.Message}");
        }
    }

    public async Task TrashMessageAsync(Connection connection, string messageId, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            await gmail.Users.Messages.Trash("me", messageId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new WorkspaceHub.Application.Common.ForbiddenException("Cần reconnect với quyền ghi.");
            }
            throw new WorkspaceHub.Application.Common.ProviderException($"Gmail API error: {ex.Message}");
        }
    }

    public async Task UntrashMessageAsync(Connection connection, string messageId, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            await gmail.Users.Messages.Untrash("me", messageId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || (ex.Error != null && ex.Error.Errors != null && ex.Error.Errors.Any(e => e.Reason != null && e.Reason.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase))))
            {
                throw new WorkspaceHub.Application.Common.ForbiddenException("Cần reconnect với quyền ghi.");
            }
            throw new WorkspaceHub.Application.Common.ProviderException($"Gmail API error: {ex.Message}");
        }
    }

    public async Task<string?> GetMessageETagAsync(Connection connection, string messageId, CancellationToken ct = default)
    {
        try
        {
            using var gmail = await BuildGmailServiceAsync(connection, ct);
            var request = gmail.Users.Messages.Get("me", messageId);
            request.Format = Google.Apis.Gmail.v1.UsersResource.MessagesResource.GetRequest.FormatEnum.Minimal;
            var msg = await request.ExecuteAsync(ct);
            return msg.HistoryId?.ToString();
        }
        catch (Google.GoogleApiException ex)
        {
            throw new WorkspaceHub.Application.Common.ProviderException($"Gmail API error: {ex.Message}");
        }
    }
}
