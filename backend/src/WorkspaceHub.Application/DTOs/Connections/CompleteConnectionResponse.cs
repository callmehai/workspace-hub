namespace WorkspaceHub.Application.DTOs.Connections;

public record ServiceConnectionItem(Guid Id, string ServiceType, bool IsEnabled);

public record CompleteConnectionResponse(
    Guid Id,
    string IntegrationKey,
    string ProviderAccountId,
    string Scopes,
    string Status,
    IReadOnlyList<ServiceConnectionItem> Services);
