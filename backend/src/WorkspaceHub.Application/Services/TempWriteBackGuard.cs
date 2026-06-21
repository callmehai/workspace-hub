using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Application.Services;

public class TempWriteBackGuard : IWriteBackGuard
{
    public void EnsureNoConflict(string? storedEtag, string? providerEtag)
    {
        Console.WriteLine($"[Writeback Guard] Stored: {storedEtag}, Provider: {providerEtag}");
        
        // TEMP - replace by SCRUM-38 (Loc)
        if (string.IsNullOrEmpty(storedEtag)) return;
        if (string.IsNullOrEmpty(providerEtag)) return;
        
        if (storedEtag != providerEtag)
        {
            throw new ConflictException("Data was modified on the provider side. Writeback conflict.");
        }
    }
}
