namespace WorkspaceHub.Application.DTOs.Admin;

/// <summary>
/// DTO user cho admin dashboard.
/// KHÔNG chứa PasswordHash — security rule (CONVENTIONS.md: never expose PasswordHash).
/// ConnectionCount = tổng tất cả Connection rows của user (mọi status).
/// ItemCount = tổng tất cả Item rows của user.
/// </summary>
public record AdminUserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    bool IsActive,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    int ConnectionCount,
    int ItemCount);
