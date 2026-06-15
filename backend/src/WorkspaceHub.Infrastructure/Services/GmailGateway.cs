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
        var gmail = await BuildGmailServiceAsync(connection, ct);
        var profile = await gmail.Users.GetProfile("me").ExecuteAsync(ct);

        return new GmailProfile(
            profile.EmailAddress,
            profile.HistoryId,
            profile.MessagesTotal,
            profile.ThreadsTotal);
    }

    public async Task<GmailMessageList> ListMessageIdsAsync(Connection connection, string? pageToken, int maxResults, CancellationToken ct = default)
    {
        var gmail = await BuildGmailServiceAsync(connection, ct);
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
        var gmail = await BuildGmailServiceAsync(connection, ct);
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
            occurredAt);
    }

    public async Task<GmailHistory> ListHistoryAsync(Connection connection, string startHistoryId, string? pageToken, CancellationToken ct = default)
    {
        if (!ulong.TryParse(startHistoryId, out ulong historyId))
        {
            return new GmailHistory(true, new List<string>(), null, null);
        }

        var gmail = await BuildGmailServiceAsync(connection, ct);
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
}
