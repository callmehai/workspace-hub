namespace WorkspaceHub.Application.DTOs.Admin;

/// <summary>
/// Query parameters cho GET /api/admin/users.
/// Default: page=1, limit=20. Search khớp Email HOẶC FullName (case-insensitive, SQL Server collation).
/// </summary>
public record GetAdminUsersRequest(
    string? Search = null,
    int Page = 1,
    int Limit = 20);
