namespace Commerce.Api.Controllers.Public;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Services.AuctionOrder;

[ApiController]
[Route("api/v1/auction-orders")]
public class AuctionOrdersController : ControllerBase
{
    private readonly IAuctionOrderService _auctionOrderService;

    public AuctionOrdersController(IAuctionOrderService auctionOrderService)
    {
        _auctionOrderService = auctionOrderService;
    }

    /// <summary>US1: Tiếp nhận settlement từ Auction Service tạo đơn đấu giá (Idempotency composite AuctionId, WinnerId).</summary>
    [HttpPost("settlement")]
    public async Task<IActionResult> ReceiveSettlement(
        [FromBody] AuctionSettlementRequest request,
        [FromServices] IConfiguration configuration,
        CancellationToken ct)
    {
        var expectedKey = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
            ?? configuration["INTERNAL_SERVICE_SECRET"]
            ?? configuration["InternalSecurity:ServiceKey"]
            ?? "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e";

        if (Request.Headers.TryGetValue("X-Internal-Service-Key", out var providedKey))
        {
            if (!string.Equals(providedKey.ToString(), expectedKey, StringComparison.Ordinal))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    errorCode = "FORBIDDEN",
                    message = "Access denied: Invalid X-Internal-Service-Key header."
                });
            }
        }

        var result = await _auctionOrderService.ReceiveSettlementAsync(request, ct);
        if (result.IsNewlyCreated)
        {
            return StatusCode(StatusCodes.Status201Created, result.Response);
        }

        return Ok(result.Response);
    }

    /// <summary>US2: Winner gửi địa chỉ giao hàng và tính phí vận chuyển GHN chốt TotalAmount.</summary>
    [HttpPut("{auctionOrderId:guid}/shipping")]
    [RequireRole("buyer", "admin", "staff")]
    public async Task<IActionResult> SubmitShipping(
        [FromRoute] Guid auctionOrderId,
        [FromBody] SubmitAuctionShippingRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var response = await _auctionOrderService.SubmitShippingAsync(auctionOrderId, buyerId, request, ct);
        return Ok(response);
    }

    /// <summary>US3: Winner thanh toán đơn đấu giá qua ví Nexus.</summary>
    [HttpPost("{auctionOrderId:guid}/pay")]
    [RequireRole("buyer", "admin", "staff")]
    public async Task<IActionResult> PayAuctionOrder(
        [FromRoute] Guid auctionOrderId,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        string? bearerToken = Request.Headers.Authorization.ToString();
        var response = await _auctionOrderService.PayAuctionOrderAsync(auctionOrderId, buyerId, bearerToken, ct);
        return Ok(response);
    }

    /// <summary>US4: Seller tiếp nhận đơn hàng đấu giá trong hạn 24 giờ.</summary>
    [HttpPost("{auctionOrderId:guid}/accept")]
    [RequireRole("seller", "admin", "staff")]
    public async Task<IActionResult> SellerAccept(
        [FromRoute] Guid auctionOrderId,
        [FromBody] SellerAcceptAuctionOrderRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var sellerId, out var authError))
        {
            return authError;
        }

        var response = await _auctionOrderService.SellerAcceptAsync(auctionOrderId, sellerId, request, ct);
        return Ok(response);
    }

    /// <summary>US5: Seller từ chối đơn hàng đấu giá và kích hoạt hoàn tiền ví 100%.</summary>
    [HttpPost("{auctionOrderId:guid}/reject")]
    [RequireRole("seller", "admin", "staff")]
    public async Task<IActionResult> SellerReject(
        [FromRoute] Guid auctionOrderId,
        [FromBody] SellerRejectAuctionOrderRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var sellerId, out var authError))
        {
            return authError;
        }

        var response = await _auctionOrderService.SellerRejectAsync(auctionOrderId, sellerId, request, ct);
        return Ok(response);
    }

    /// <summary>US8: Buyer xác nhận đã nhận hàng đấu giá và giải ngân Escrow cho Seller.</summary>
    [HttpPost("{auctionOrderId:guid}/confirm-receipt")]
    [RequireRole("buyer", "admin", "staff")]
    public async Task<IActionResult> ConfirmReceipt(
        [FromRoute] Guid auctionOrderId,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var response = await _auctionOrderService.ConfirmReceiptAsync(auctionOrderId, buyerId, ct);
        return Ok(response);
    }

    /// <summary>US9: Tra cứu chi tiết đơn hàng đấu giá.</summary>
    [HttpGet("{auctionOrderId:guid}")]
    [RequireRole("buyer", "seller", "admin", "staff")]
    public async Task<IActionResult> GetOrderDetail(
        [FromRoute] Guid auctionOrderId,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var response = await _auctionOrderService.GetOrderDetailAsync(auctionOrderId, currentUserId, ct);
        return Ok(response);
    }

    /// <summary>US9: Tra cứu danh sách đơn hàng đấu giá của Seller (có phân trang).</summary>
    [HttpGet("seller")]
    [RequireRole("seller", "admin", "staff")]
    public async Task<IActionResult> ListSellerOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var sellerId, out var authError))
        {
            return authError;
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var response = await _auctionOrderService.ListSellerOrdersAsync(sellerId, page, pageSize, ct);
        return Ok(response);
    }
}
