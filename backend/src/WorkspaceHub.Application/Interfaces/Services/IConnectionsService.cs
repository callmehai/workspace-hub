using WorkspaceHub.Application.DTOs.Connections;
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
    Task<CompleteConnectionResponse> CompleteConnectionAsync(
        string code,
        string state,
        Guid userId,
        CancellationToken ct = default);

    Task<IntegrationResponse> ToggleIntegrationAsync(string key, bool isEnabled, CancellationToken ct = default);

    /// <summary>
    /// SCRUM-14: Lấy danh sách connections của user (token masked).
    /// </summary>
    Task<IReadOnlyList<ConnectionDto>> GetConnectionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// SCRUM-14: Ngắt kết nối — xoá Connection, Items.ConnectionId SET NULL, delete ScheduledEmails.
    /// FK Items/ScheduledEmails → Connections là NoAction ở DB nên phải xử lý ở service layer.
    /// </summary>
    Task DisconnectAsync(Guid connectionId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// SCRUM-14: Refresh token của connection. 422 nếu refresh token invalid → set Status = Error.
    /// </summary>
    Task<RefreshConnectionResponse> RefreshConnectionAsync(Guid connectionId, Guid userId, CancellationToken ct = default);

    Task<ManualSyncResult> TriggerManualSyncAsync(Guid connectionId, Guid userId, CancellationToken ct = default);
}

