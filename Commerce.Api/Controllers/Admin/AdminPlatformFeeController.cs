namespace Commerce.Api.Controllers.Admin;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

[ApiController]
[Route("api/v1/admin/platform-fee")]
[RequireRole("staff", "admin")]
public class AdminPlatformFeeController : ControllerBase
{
    public const string PlatformFeeMetadataCacheKey = "commerce:platform_fee:metadata";
    public const string PlatformFeePercentCacheKey = "commerce:platform_fee_percent";

    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminPlatformFeeController> _logger;

    public AdminPlatformFeeController(
        IDistributedCache cache,
        IConfiguration configuration,
        ILogger<AdminPlatformFeeController> logger)
    {
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/v1/admin/platform-fee
    /// Retrieves the currently active platform markup fee percentage.
    /// Restricted strictly to staff and admin roles.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PlatformFeeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPlatformFee(CancellationToken ct)
    {
        var cachedMetadata = await _cache.GetStringAsync(PlatformFeeMetadataCacheKey, ct);
        if (!string.IsNullOrEmpty(cachedMetadata))
        {
            try
            {
                var response = JsonSerializer.Deserialize<PlatformFeeResponse>(cachedMetadata);
                if (response != null)
                {
                    return Ok(response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached platform fee metadata.");
            }
        }

        var defaultPercent = _configuration.GetValue<decimal>("PlatformFee:DefaultPercent", 0.0m);
        var fallbackResponse = new PlatformFeeResponse(
            DefaultPercent: defaultPercent,
            UpdatedByRole: "system",
            UpdatedAt: DateTimeOffset.UtcNow
        );

        return Ok(fallbackResponse);
    }

    /// <summary>
    /// PUT /api/v1/admin/platform-fee
    /// Updates the platform markup fee percentage used for subsequent order calculations.
    /// Restricted strictly to staff and admin roles.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(PlatformFeeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdatePlatformFee(
        [FromBody] UpdatePlatformFeeRequest request,
        CancellationToken ct)
    {
        if (request.DefaultPercent < 0.0m || request.DefaultPercent > 100.0m)
        {
            return BadRequest(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                title = "Bad Request",
                status = StatusCodes.Status400BadRequest,
                errorCode = "VAL_INVALID_INPUT",
                detail = "Platform fee rate must be between 0% and 100%.",
                traceId = HttpContext.GetTraceId()
            });
        }

        var userRoles = HttpContext.GetUserRoles();
        var updatedByRole = userRoles.Contains("admin", StringComparer.OrdinalIgnoreCase)
            ? "admin"
            : (userRoles.Contains("staff", StringComparer.OrdinalIgnoreCase) ? "staff" : "staff");

        var response = new PlatformFeeResponse(
            DefaultPercent: request.DefaultPercent,
            UpdatedByRole: updatedByRole,
            UpdatedAt: DateTimeOffset.UtcNow
        );

        var serialized = JsonSerializer.Serialize(response);
        await _cache.SetStringAsync(PlatformFeeMetadataCacheKey, serialized, ct);
        await _cache.SetStringAsync(PlatformFeePercentCacheKey, request.DefaultPercent.ToString("G"), ct);

        _logger.LogInformation("Platform fee updated to {Percent}% by role '{Role}'.",
            request.DefaultPercent, updatedByRole);

        return Ok(response);
    }
}
