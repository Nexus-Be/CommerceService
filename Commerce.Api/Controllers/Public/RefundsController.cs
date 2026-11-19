namespace Commerce.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Services.Order;
using Commerce.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/refunds")]
[RequireRole("buyer", "seller", "admin", "staff")]
public class RefundsController : ControllerBase
{
    private readonly IOrderService _orderService;

    public RefundsController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>Tra cứu danh sách yêu cầu hoàn tiền có phân trang database và bộ lọc.</summary>
    [HttpGet]
    public async Task<IActionResult> ListRefunds(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] RefundStatus? status = null,
        [FromQuery] Guid? subOrderId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
        var response = await _orderService.ListRefundsAsync(currentUserId, page, pageSize, status, subOrderId, isAdminOrStaff, ct);
        return Ok(response);
    }

    /// <summary>Tra cứu chi tiết yêu cầu hoàn tiền theo ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRefundDetail(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
        var response = await _orderService.GetRefundDetailAsync(currentUserId, id, isAdminOrStaff, ct);
        return Ok(response);
    }

    /// <summary>Người bán hoặc Admin duyệt yêu cầu hoàn tiền và hoàn tiền ví cho người mua.</summary>
    [HttpPost("{id:guid}/approve")]
    [RequireRole("seller", "admin", "staff")]
    public async Task<IActionResult> ApproveRefund(
        [FromRoute] Guid id,
        [FromBody] ApproveRefundRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
        var response = await _orderService.ApproveRefundAsync(currentUserId, id, request, isAdminOrStaff, ct);
        return Ok(response);
    }

    /// <summary>Người bán hoặc Admin từ chối yêu cầu hoàn tiền kèm lý do bắt buộc.</summary>
    [HttpPost("{id:guid}/reject")]
    [RequireRole("seller", "admin", "staff")]
    public async Task<IActionResult> RejectRefund(
        [FromRoute] Guid id,
        [FromBody] RejectRefundRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
        var response = await _orderService.RejectRefundAsync(currentUserId, id, request, isAdminOrStaff, ct);
        return Ok(response);
    }
}
