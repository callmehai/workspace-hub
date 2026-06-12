using WorkspaceHub.Application.OAuth.Core;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Nghiệp vụ OAuth connection: build authorization URL, exchange code, disconnect.</summary>
public interface IConnectionsService
{
    /// <summary>
    /// Tra Integration, decrypt ClientId, dispatch sang đúng IProviderStrategy, cache CSRF state.
    /// serviceType: tên ServiceType enum ("Gmail"/"GCal"/"Drive"/"Jira") — mỗi lần chỉ connect 1 service.
    /// </summary>
    Task<InitiateConnectionResult> InitiateConnectionAsync(
        string integrationKey,
        string serviceType,
        string redirectUri,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Verify CSRF state, exchange code → token, persist Connections (mô hình B: 1 row mỗi service được cấp).
    /// </summary>
    Task<CompleteConnectionResult> CompleteConnectionAsync(
        string code,
        string state,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>Encrypt clientId + clientSecret rồi lưu vào Integration. (Admin-only — controller đã gắn [Authorize(Roles="Admin")].)</summary>
    Task SetCredentialsAsync(
        string integrationKey,
        string clientId,
        string clientSecret,
        CancellationToken ct = default);

    // TODO SCRUM-14 (DisconnectAsync): FK Items/ScheduledEmails → Connections là NoAction ở DB,
    // nên trước khi xoá Connection PHẢI: (1) UPDATE Items SET ConnectionId = NULL,
    // (2) cancel/xoá ScheduledEmails Pending của connection đó — xoá thẳng sẽ FK violation.
}
