namespace WorkspaceHub.Application.Abstractions;

public record SyncResult(int Scanned, int Created, int Skipped, string? NewCursor);
