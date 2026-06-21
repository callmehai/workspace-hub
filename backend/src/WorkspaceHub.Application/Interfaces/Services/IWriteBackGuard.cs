namespace WorkspaceHub.Application.Interfaces.Services;

public interface IWriteBackGuard
{
    void EnsureNoConflict(string? storedEtag, string? providerEtag);
}
