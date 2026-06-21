using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Application.Services;

public class TempWriteBackGuard : IWriteBackGuard
{
    private readonly ILogger<TempWriteBackGuard> _logger;

    public TempWriteBackGuard(ILogger<TempWriteBackGuard> logger)
    {
        _logger = logger;
    }

    public void EnsureNoConflict(string? storedEtag, string? providerEtag)
    {
        _logger.LogInformation("[Writeback Guard] Stored: {StoredEtag}, Provider: {ProviderEtag}", storedEtag, providerEtag);
        
        // TEMP - replace by SCRUM-38 (Loc)
        if (string.IsNullOrEmpty(storedEtag)) return;
        if (string.IsNullOrEmpty(providerEtag)) return;
        
        if (storedEtag != providerEtag)
        {
            throw new ConflictException("Data was modified on the provider side. Writeback conflict.");
        }
    }
}
