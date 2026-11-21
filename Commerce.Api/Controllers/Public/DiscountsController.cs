namespace Commerce.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Common.Models;
using Commerce.Application.Services.Discount;
using Commerce.Domain.Enums;

[ApiController]
[Route("api/v1/discounts")]
public class DiscountsController : ControllerBase
{
    private readonly IDiscountService _discountService;

    public DiscountsController(IDiscountService discountService)
    {
        _discountService = discountService;
    }

    /// <summary>UC-01: Get Available Vouchers for Buyer with Eligibility Evaluation.</summary>
    [HttpGet("available")]
    [RequireRole("buyer", "admin", "staff")]
    public async Task<IActionResult> GetAvailableVouchers(
        [FromQuery] Guid? sellerId,
        [FromQuery] string? cartItemIds,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        List<Guid>? parsedCartItemIds = null;
        if (!string.IsNullOrWhiteSpace(cartItemIds))
        {
            parsedCartItemIds = cartItemIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(idStr => Guid.TryParse(idStr, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToList();
        }

        var result = await _discountService.GetAvailableVouchersAsync(
            buyerId, sellerId, parsedCartItemIds, page, pageSize, ct);

        return Ok(result);
    }

    /// <summary>UC-02: Validate a Single Voucher Code.</summary>
    [HttpPost("validate")]
    [RequireRole("buyer", "admin", "staff")]
    public async Task<IActionResult> ValidateVoucher(
        [FromBody] ValidateVoucherRequest request,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var result = await _discountService.ValidateVoucherAsync(buyerId, request, ct);
        return Ok(result);
    }

    /// <summary>UC-03: Recommend Optimal Voucher Combination.</summary>
    [HttpPost("recommend")]
    [RequireRole("buyer", "admin", "staff")]
    public async Task<IActionResult> RecommendVouchers(
        [FromBody] RecommendVouchersRequest request,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var result = await _discountService.RecommendVouchersAsync(buyerId, request, ct);
        return Ok(result);
    }

    /// <summary>UC-04: Create a New Promotion Voucher.</summary>
    [HttpPost]
    [RequireRole("admin", "seller")]
    public async Task<IActionResult> CreateDiscount(
        [FromBody] CreateDiscountRequest request,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdmin = this.IsAdmin();
        var result = await _discountService.CreateDiscountAsync(currentUserId, isAdmin, request, ct);

        return CreatedAtAction(nameof(GetDiscountById), new { discountId = result.Id }, result);
    }

    /// <summary>UC-05: List Promotions with Status and Scope Filtering.</summary>
    [HttpGet]
    [RequireRole("admin", "seller", "staff")]
    public async Task<IActionResult> ListDiscounts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DiscountScopeType? scopeType = null,
        [FromQuery] DiscountStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? ownerSellerId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdmin = this.IsAdmin() || this.HasRole("staff");
        var result = await _discountService.ListDiscountsAsync(
            currentUserId, isAdmin, page, pageSize, scopeType, status, search, ownerSellerId, ct);

        return Ok(result);
    }

    /// <summary>UC-06: Get Promotion Details.</summary>
    [HttpGet("{discountId:guid}")]
    [RequireRole("admin", "seller", "staff")]
    public async Task<IActionResult> GetDiscountById(
        [FromRoute] Guid discountId,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdmin = this.IsAdmin() || this.HasRole("staff");
        var result = await _discountService.GetDiscountByIdAsync(currentUserId, isAdmin, discountId, ct);

        return Ok(result);
    }

    /// <summary>UC-07: Update Promotion Voucher Metadata.</summary>
    [HttpPut("{discountId:guid}")]
    [RequireRole("admin", "seller")]
    public async Task<IActionResult> UpdateDiscount(
        [FromRoute] Guid discountId,
        [FromBody] UpdateDiscountRequest request,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdmin = this.IsAdmin();
        var result = await _discountService.UpdateDiscountAsync(currentUserId, isAdmin, discountId, request, ct);

        return Ok(result);
    }

    /// <summary>UC-08: Toggle Promotion Voucher Status.</summary>
    [HttpPatch("{discountId:guid}/status")]
    [RequireRole("admin", "seller")]
    public async Task<IActionResult> UpdateDiscountStatus(
        [FromRoute] Guid discountId,
        [FromBody] UpdateDiscountStatusRequest request,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdmin = this.IsAdmin();
        var result = await _discountService.UpdateDiscountStatusAsync(currentUserId, isAdmin, discountId, request, ct);

        return Ok(result);
    }

    /// <summary>UC-09: Soft Delete a Promotion Voucher.</summary>
    [HttpDelete("{discountId:guid}")]
    [RequireRole("admin", "seller")]
    public async Task<IActionResult> DeleteDiscount(
        [FromRoute] Guid discountId,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdmin = this.IsAdmin();
        await _discountService.DeleteDiscountAsync(currentUserId, isAdmin, discountId, ct);

        return NoContent();
    }
}
