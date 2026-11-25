namespace Commerce.Api.Controllers.Admin;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Services.Delivery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

public sealed record RevokeBuyerRefusalPenaltyRequest([property: Required, StringLength(500, MinimumLength = BuyerRefusalPenaltyService.MinReasonLength)] string Reason);

/// <summary>
/// Withdrawing the reputation penalty of a buyer who refused a delivery. Only an admin decides; staff can record the complaint but cannot
/// change a score. The call is safe to repeat: the first one stores who, why and when, later ones return the state that is already there.
/// </summary>
[ApiController]
[Route("api/v1/admin/buyer-refusal-penalties")]
[RequireRole("admin")]
public class AdminBuyerRefusalPenaltiesController : ControllerBase
{
    private readonly BuyerRefusalPenaltyService _penalties;

    public AdminBuyerRefusalPenaltiesController(BuyerRefusalPenaltyService penalties)
    {
        _penalties = penalties;
    }

    /// <summary>
    /// POST /api/v1/admin/buyer-refusal-penalties/{subOrderId}/revoke
    /// 200 when User confirmed (Revoked: points given back, Cancelled: the penalty had taken none) and 202 while the sub-order waits for User
    /// (RevokePending: the worker repeats the call until User answers).
    /// </summary>
    [HttpPost("{subOrderId:guid}/revoke")]
    [ProducesResponseType(typeof(PenaltyRevocationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PenaltyRevocationResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revoke(
        [FromRoute] Guid subOrderId,
        [FromBody] RevokeBuyerRefusalPenaltyRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var adminId, out var authError))
        {
            return authError;
        }

        var result = await _penalties.RequestRevocationAsync(subOrderId, adminId, request.Reason, ct);
        return result.Confirmed ? Ok(result) : StatusCode(StatusCodes.Status202Accepted, result);
    }
}
