namespace WorkspaceHub.Application.DTOs.Connections;

public record ConnectionItem(Guid Id, string ServiceType, string Status);

public record CompleteConnectionResponse(
    string IntegrationKey,
    string ProviderAccountId,
    IReadOnlyList<ConnectionItem> Connections);
