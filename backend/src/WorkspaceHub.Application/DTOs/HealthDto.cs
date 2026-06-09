namespace WorkspaceHub.Application.DTOs;

/// <summary>Kết quả health-check trả ra API.</summary>
public record HealthDto(
    string Status,        // "Healthy" | "Degraded"
    string Database,      // "Connected" | "Unreachable"
    int UserCount,
    DateTime ServerTimeUtc);
