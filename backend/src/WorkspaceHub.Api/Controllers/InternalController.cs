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
    private readonly IProcessScheduledEmailsService _processor;
    private readonly IConfiguration _config;

    public InternalController(IProcessScheduledEmailsService processor, IConfiguration config)
    {
        _processor = processor;
        _config = config;
    }

    [HttpPost("process-scheduled")]
    public async Task<IActionResult> ProcessScheduled(
        [FromHeader(Name = "X-Cron-Secret")] string? cronSecret,
        CancellationToken ct)
    {
        var configured = _config["Cron:Secret"];

        // Chưa cấu hình secret → từ chối (tránh để endpoint mở toang khi quên set).
        if (string.IsNullOrEmpty(configured))
        {
            throw new UnauthorizedException("Cron secret is not configured.");
        }

        if (string.IsNullOrEmpty(cronSecret) || !FixedTimeEquals(cronSecret, configured))
        {
            throw new UnauthorizedException("Invalid or missing cron secret.");
        }

        var result = await _processor.ProcessDueEmailsAsync(ct: ct);
        return Ok(result);
    }

    /// <summary>So sánh constant-time tránh timing attack khi đối chiếu secret.</summary>
    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
}
