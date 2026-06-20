namespace WorkspaceHub.Application.Abstractions;

public record GmailSampleDto(
    string? ExternalId,
    string Type,
    string Title,
    string? Snippet,
    bool IsImportant,
    DateTime OccurredAt,
    string MetadataJson);
