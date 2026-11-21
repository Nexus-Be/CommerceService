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
