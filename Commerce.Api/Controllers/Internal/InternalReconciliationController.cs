namespace Commerce.Api.Controllers.Internal;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Commerce.Application.Interfaces;

/// <summary>Read-only order and refund data for the wallet service's reconciliation. It never changes anything.</summary>
[ApiController]
[Route("api/v1/internal/reconciliation")]
public sealed class InternalReconciliationController : ControllerBase
{
    private readonly ICommerceDbContext _db;
    private readonly IConfiguration _configuration;

    public InternalReconciliationController(ICommerceDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public sealed record SubOrderRecon(
        Guid SubOrderId, string Status, string PaymentMethod, decimal ItemsAmount, decimal ProductDiscountAmount, decimal ShopDiscountAmount,
        decimal SystemDiscountAmount, decimal ShippingFeeAmount, decimal TotalAmount, decimal SellerReceiveAmount, decimal PlatformFeeAmount,
        bool IsEscrowSettled, DateTimeOffset? PaidAt);

    public sealed record RefundRecon(Guid RefundId, Guid? SubOrderId, Guid? OrderId, decimal Amount, string Status, Guid? WalletTransactionId, Guid? RefundTransactionId);

    public sealed record OrderRecon(Guid OrderId, Guid BuyerId, string Status, decimal TotalAmount, DateTimeOffset UpdatedAt, SubOrderRecon[] SubOrders, RefundRecon[] Refunds);

    public sealed record OrderReconPage(OrderRecon[] Items, Guid? NextCursor);

    /// <summary>Lists orders with their sub-orders and refunds, ordered by id, starting after the cursor.</summary>
    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders(
        [FromQuery] DateTimeOffset? updatedFrom = null,
        [FromQuery] Guid? cursor = null,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        if (Denied() is { } denied) return denied;
        limit = limit is > 0 and <= 500 ? limit : 200;

        var query = _db.Orders.AsNoTracking().Where(o => !o.IsDeleted);
        if (updatedFrom.HasValue) query = query.Where(o => o.UpdatedAt >= updatedFrom.Value);
        if (cursor.HasValue) query = query.Where(o => o.Id.CompareTo(cursor.Value) > 0);

        var orders = await query
            .OrderBy(o => o.Id)
            .Take(limit)
            .Select(o => new
            {
                o.Id, o.BuyerId, o.Status, o.TotalAmount, o.UpdatedAt,
                SubOrders = o.SubOrders.Select(s => new SubOrderRecon(
                    s.Id, s.Status.ToString(), s.PaymentMethod.ToString(), s.ItemsAmount, s.ProductDiscountAmount, s.ShopDiscountAmount,
                    s.SystemDiscountAmount, s.ShippingFeeAmount, s.TotalAmount, s.SellerReceiveAmount, s.PlatformFeeAmount, s.IsEscrowSettled, s.PaidAt)).ToList(),
                Refunds = o.Refunds.Select(r => new RefundRecon(r.Id, r.SubOrderId, r.OrderId, r.Amount, r.Status.ToString(), r.WalletTransactionId, r.RefundTransactionId)).ToList()
            })
            .ToListAsync(ct);

        var items = orders.Select(o => new OrderRecon(o.Id, o.BuyerId, o.Status.ToString(), o.TotalAmount, o.UpdatedAt, o.SubOrders.ToArray(), o.Refunds.ToArray())).ToArray();
        var next = items.Length == limit ? items[^1].OrderId : (Guid?)null;
        return Ok(new OrderReconPage(items, next));
    }

    public sealed record LegacyClassItem(string Class, Guid Id, decimal SellerReceiveAmount, decimal PlatformFeeAmount, string Detail);

    public sealed record LegacySettlementClasses(System.Collections.Generic.Dictionary<string, int> Counts, LegacyClassItem[] Items);

    /// <summary>
    /// Classifies orders that are on the legacy payout protocol (V1) or whose V2 payout needs a finance decision, so the old data can be
    /// reviewed and a fix approved per group. It only reads: it never pays, adjusts a wallet or recalculates a fee.
    /// </summary>
    [HttpGet("legacy-settlement-classes")]
    public async Task<IActionResult> GetLegacySettlementClasses([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (Denied() is { } denied) return denied;
        limit = limit is > 0 and <= 500 ? limit : 100;

        var walletV1 = _db.SubOrders.AsNoTracking().Where(s => !s.IsDeleted && s.SettlementProtocolVersion < 2 && s.PaymentMethod == Commerce.Domain.Enums.PaymentMethod.Wallet
            && s.Status == Commerce.Domain.Enums.SubOrderStatus.Completed);
        var unpaidV1 = walletV1.Where(s => !s.IsEscrowSettled && s.SellerReceiveAmount > 0m);
        var auctionUnpaid = _db.AuctionOrders.AsNoTracking().Where(a => !a.IsDeleted && a.Status == Commerce.Domain.Enums.SubOrderStatus.Completed && a.PayoutCompletedAt == null);
        var auctionFractionalFee = _db.AuctionOrders.AsNoTracking().Where(a => !a.IsDeleted && a.PlatformFeeAmount != Math.Round(a.PlatformFeeAmount, 0));
        var needsFinance = _db.SettlementSnapshots.AsNoTracking().Where(n => n.Status == Commerce.Domain.Entities.SettlementSnapshotStatus.NeedsFinance);

        var items = new System.Collections.Generic.List<LegacyClassItem>();
        // Unpaid V1 sub-orders with a platform discount have no reserved subsidy, so they cannot move to V2 without finance providing the source.
        var noSubsidy = unpaidV1.Where(s => s.SystemDiscountAmount > 0m);
        var convertible = unpaidV1.Where(s => s.SystemDiscountAmount == 0m);
        items.AddRange((await noSubsidy.OrderBy(s => s.CompletedAt).Take(limit).ToListAsync(ct)).Select(s =>
            new LegacyClassItem("V1_UNPAID_PLATFORM_DISCOUNT", s.Id, s.SellerReceiveAmount, s.PlatformFeeAmount, "Unpaid legacy order with a platform discount and no reserved subsidy: needs a funding source before it can be paid.")));
        items.AddRange((await convertible.OrderBy(s => s.CompletedAt).Take(limit).ToListAsync(ct)).Select(s =>
            new LegacyClassItem("V1_UNPAID_CONVERTIBLE", s.Id, s.SellerReceiveAmount, s.PlatformFeeAmount, "Unpaid legacy order that can continue through protocol V2.")));
        items.AddRange((await auctionUnpaid.OrderBy(a => a.CompletedAt).Take(limit).ToListAsync(ct)).Select(a =>
            new LegacyClassItem("AUCTION_PAYOUT_PENDING", a.Id, a.SellerReceiveAmount, a.PlatformFeeAmount, "Completed auction order whose payout was never confirmed.")));
        items.AddRange((await auctionFractionalFee.OrderBy(a => a.CreatedAt).Take(limit).ToListAsync(ct)).Select(a =>
            new LegacyClassItem("AUCTION_FRACTIONAL_FEE", a.Id, a.SellerReceiveAmount, a.PlatformFeeAmount, "Auction fee with a fractional VND amount. Reported only: the fee is never recalculated.")));
        items.AddRange((await needsFinance.OrderBy(n => n.CreatedAt).Take(limit).ToListAsync(ct)).Select(n =>
            new LegacyClassItem("PAYOUT_NEEDS_FINANCE", n.SubOrderId, n.SellerReceiveAmount, n.PlatformFeeAmount, $"{n.LastErrorCode}: {n.LastError}")));

        var counts = new System.Collections.Generic.Dictionary<string, int>
        {
            ["V1_UNPAID_PLATFORM_DISCOUNT"] = await noSubsidy.CountAsync(ct),
            ["V1_UNPAID_CONVERTIBLE"] = await convertible.CountAsync(ct),
            ["V1_PAID"] = await walletV1.CountAsync(s => s.IsEscrowSettled, ct),
            ["AUCTION_PAYOUT_PENDING"] = await auctionUnpaid.CountAsync(ct),
            ["AUCTION_FRACTIONAL_FEE"] = await auctionFractionalFee.CountAsync(ct),
            ["PAYOUT_NEEDS_FINANCE"] = await needsFinance.CountAsync(ct)
        };
        return Ok(new LegacySettlementClasses(counts, items.ToArray()));
    }

    private IActionResult? Denied()
    {
        var expected = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
            ?? _configuration["INTERNAL_SERVICE_SECRET"]
            ?? _configuration["InternalSecurity:ServiceKey"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            return StatusCode(500, new { errorCode = "INTERNAL_CONFIGURATION_ERROR", message = "Internal service authentication is not configured." });
        }

        var provided = Request.Headers["X-Internal-Service-Key"].ToString();
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        if (a.Length == 0 || !CryptographicOperations.FixedTimeEquals(a, b))
        {
            return StatusCode(403, new { errorCode = "FORBIDDEN", message = "Access denied to internal service resource." });
        }

        return null;
    }
}
