using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Endpoint nội bộ cho cron job (SCRUM-31). KHÔNG dùng JWT — bảo vệ bằng header
/// X-Cron-Secret so khớp config "Cron:Secret" (CsrfMiddleware đã exempt path này).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/internal")]
public class InternalController : ControllerBase
{
    private readonly IProcessScheduledEmailsService _emailProcessor;
    private readonly IProcessConnectionsSyncService _syncProcessor;
    private readonly IConfiguration _config;

    public InternalController(
        IProcessScheduledEmailsService emailProcessor,
        IProcessConnectionsSyncService syncProcessor,
        IConfiguration config)
    {
        _emailProcessor = emailProcessor;
        _syncProcessor = syncProcessor;
        _config = config;
    }

    [HttpPost("process-scheduled")]
    public async Task<IActionResult> ProcessScheduled(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken ct)
    {
        ValidateCronSecret(cronSecret);
        var result = await _emailProcessor.ProcessDueEmailsAsync(ct: ct);
        return Ok(result);
    }

    
    [HttpPost("process-sync")]
    public async Task<IActionResult> ProcessSync(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken ct)
    {
        ValidateCronSecret(cronSecret);
        var result = await _syncProcessor.ProcessConnectionsSyncAsync(ct);
        return Ok(result);
    }

    private void ValidateCronSecret(string? cronSecret)
    {
        var configured = _config["Cron:Secret"];
        if (string.IsNullOrEmpty(configured))
            throw new UnauthorizedException("Cron secret is not configured.");

        if (string.IsNullOrEmpty(cronSecret) || !FixedTimeEquals(cronSecret, configured))
            throw new UnauthorizedException("Invalid or missing cron secret.");
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
