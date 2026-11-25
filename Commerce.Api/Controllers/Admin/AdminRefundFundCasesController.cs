namespace Commerce.Api.Controllers.Admin;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Services.RefundFund;
using Commerce.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

public sealed class RequestRefundFundCaseRequest
{
    [Required(ErrorMessage = "The sub-order is required.")]
    public Guid SubOrderId { get; init; }

    [Required(ErrorMessage = "A reason is required.")]
    [StringLength(500, MinimumLength = RefundFundService.MinTextLength, ErrorMessage = "The reason must be between 10 and 500 characters long.")]
    public string Reason { get; init; } = string.Empty;

    public Guid? ReportId { get; init; }
}

public sealed class ResolveRefundFundCaseRequest
{
    [Required(ErrorMessage = "A note is required.")]
    [StringLength(500, MinimumLength = RefundFundService.MinTextLength, ErrorMessage = "The note must be between 10 and 500 characters long.")]
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// Refunding a buyer from the refund fund after the order was paid out to the seller. Staff can read the list (the NeedsFinance queue); only
/// an admin decides, approves, retries or closes a case. [RequireRole] lets staff through every admin route, so the actions check the admin
/// role themselves.
/// </summary>
[ApiController]
[Route("api/v1/admin/refund-fund/cases")]
[RequireRole("staff", "admin")]
public class AdminRefundFundCasesController : ControllerBase
{
    private readonly RefundFundService _service;

    public AdminRefundFundCasesController(RefundFundService service)
    {
        _service = service;
    }

    /// <summary>Lists the cases, newest first; filter by status (for example NeedsFinance).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] RefundFundCaseStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await _service.ListAsync(status, page, pageSize, ct));

    /// <summary>Decides to refund a sub-order from the fund. Above the threshold the case waits for a second, different admin.</summary>
    [HttpPost]
    public async Task<IActionResult> Request([FromBody] RequestRefundFundCaseRequest request, CancellationToken ct)
    {
        if (!TryAdmin(out var adminId, out var error)) return error;
        return Ok(await _service.RequestAsync(request.SubOrderId, adminId, request.Reason, request.ReportId, ct));
    }

    /// <summary>The second admin approves a case above the threshold.</summary>
    [HttpPost("{subOrderId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid subOrderId, CancellationToken ct)
    {
        if (!TryAdmin(out var adminId, out var error)) return error;
        return Ok(await _service.ApproveSecondAsync(subOrderId, adminId, ct));
    }

    /// <summary>Asks the Wallet again for a case in NeedsFinance, for example after the fund was topped up.</summary>
    [HttpPost("{subOrderId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid subOrderId, CancellationToken ct)
    {
        if (!TryAdmin(out var adminId, out var error)) return error;
        return Ok(await _service.RetryAsync(subOrderId, adminId, ct));
    }

    /// <summary>Closes a NeedsFinance case after a person handled it outside the system.</summary>
    [HttpPost("{subOrderId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid subOrderId, [FromBody] ResolveRefundFundCaseRequest request, CancellationToken ct)
    {
        if (!TryAdmin(out var adminId, out var error)) return error;
        return Ok(await _service.ResolveAsync(subOrderId, adminId, request.Note, ct));
    }

    private bool TryAdmin(out Guid adminId, out IActionResult error)
    {
        adminId = Guid.Empty;
        error = null!;
        if (!this.TryGetUserId(out adminId, out var authError))
        {
            error = authError;
            return false;
        }

        if (!this.IsAdmin())
        {
            error = this.ErrorResult(StatusCodes.Status403Forbidden, "FORBIDDEN", "Only an admin can decide on a refund from the refund fund.");
            return false;
        }

        return true;
    }
}
