namespace WorkspaceHub.Domain.Entities;

/// <summary>Catalog provider OAuth (blueprint). Seed sẵn 1 row Google.</summary>
public class Integration
{
    public Guid Id { get; set; }
    public string Key { get; set; } = null!;            // slug duy nhất, route callback (vd "google")
    public string DisplayName { get; set; } = null!;
    public string IconUrl { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Provider { get; set; } = null!;       // nhóm provider
    public string ClientIdEncrypted { get; set; } = null!;
    public string ClientSecretEncrypted { get; set; } = null!;
    public string AuthorizationEndpoint { get; set; } = null!;
    public string TokenEndpoint { get; set; } = null!;
    public string SupportedServices { get; set; } = null!; // JSON: ["Gmail","GCal","Drive"]
    public bool IsEnabled { get; set; } = true;

    // Navigation
    public ICollection<Connection> Connections { get; set; } = new List<Connection>();
}
