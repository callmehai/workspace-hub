namespace WorkspaceHub.Application.DTOs.Connections;

public class IntegrationResponse
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }

    public IntegrationResponse() { }

    public IntegrationResponse(Guid id, string key, string displayName, bool isEnabled)
    {
        Id = id;
        Key = key;
        DisplayName = displayName;
        IsEnabled = isEnabled;
    }
}
