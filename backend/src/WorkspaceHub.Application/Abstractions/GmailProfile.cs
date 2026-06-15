namespace WorkspaceHub.Application.Abstractions;

public record GmailProfile(
    string EmailAddress,
    ulong? HistoryId,
    long? MessagesTotal,
    long? ThreadsTotal);
