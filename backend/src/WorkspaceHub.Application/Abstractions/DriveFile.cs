namespace WorkspaceHub.Application.Abstractions;

public record DriveFile(
    string Id,
    string? ETag,
    string? Name,
    string? MimeType);
