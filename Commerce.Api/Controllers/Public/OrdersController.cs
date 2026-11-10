namespace Commerce.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Services.Order;
using Commerce.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/orders")]
[RequireRole("buyer", "seller", "admin", "staff")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>R75: Tạo phiên báo giá thanh toán xem trước (tính tiền ship GHN, voucher, 15m TTL).</summary>
    [HttpPost("/api/v1/checkouts/quote")]
    public async Task<IActionResult> QuoteCheckout([FromBody] CheckoutQuoteRequest request, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var response = await _orderService.QuoteCheckoutAsync(buyerId, request, ct);
        return Ok(response);
    }

    /// <summary>R76: Tạo đơn hàng chính thức từ mã báo giá quoteId.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var response = await _orderService.CreateOrderAsync(buyerId, request, ct);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>R77: Tra cứu chi tiết đơn hàng cha theo ID.</summary>
    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> GetOrderDetail([FromRoute] Guid orderId, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var order = await _orderService.GetOrderDetailAsync(currentUserId, orderId, ct);
        return Ok(order);
    }

    /// <summary>R78: Lịch sử đơn hàng của người mua đang đăng nhập (hỗ trợ phân trang và lọc status).</summary>
    [HttpGet]
    public async Task<IActionResult> ListOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderStatus? status = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var orders = await _orderService.ListOrdersAsync(currentUserId, page, pageSize, status, ct);
        return Ok(orders);
    }

    /// <summary>Người bán tra cứu danh sách đơn hàng con thuộc Shop của mình, phân trang tại DB.</summary>
    [HttpGet("/api/v1/sub-orders")]
    public async Task<IActionResult> ListSellerSubOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] SubOrderStatus? status = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var sellerId, out var authError))
        {
            return authError;
        }

        var response = await _orderService.ListSellerSubOrdersAsync(sellerId, page, pageSize, status, ct);
        return Ok(response);
    }

    /// <summary>R79: Tra cứu chi tiết đơn hàng con của Shop (Single DTO có discount breakdown và địa chỉ JSONB).</summary>
    [HttpGet("/api/v1/sub-orders/{subOrderId:guid}")]
    public async Task<IActionResult> GetSubOrderDetail([FromRoute] Guid subOrderId, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var subOrder = await _orderService.GetSubOrderDetailAsync(currentUserId, subOrderId, ct);
        return Ok(subOrder);
    }

    /// <summary>R80: Người mua hoặc Người bán hủy đơn hàng / đơn con khi chưa giao.</summary>
    [HttpPost("{orderId:guid}/cancel")]
    [HttpPut("{orderId:guid}/cancel")]
    public async Task<IActionResult> CancelOrder(
        [FromRoute] Guid orderId,
        [FromBody] CancelOrderRequest? request = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var effectiveRequest = request ?? new CancelOrderRequest(CancellationReasonCode.BuyerChangedMind, "Buyer cancelled order");
        await _orderService.CancelOrderAsync(currentUserId, orderId, effectiveRequest, ct);
        return Ok(new { orderId, status = "Cancelled", cancelledAt = DateTimeOffset.UtcNow });
    }

    /// <summary>R83: Người bán tiếp nhận đơn hàng con (SubOrderStatus to Confirmed).</summary>
    [HttpPost("{orderId:guid}/accept")]
    public async Task<IActionResult> SellerAcceptOrder(
        [FromRoute] Guid orderId,
        [FromBody] SellerAcceptOrderRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var sellerId, out var authError))
        {
            return authError;
        }

        await _orderService.SellerAcceptSubOrderAsync(sellerId, orderId, request, ct);
        return Ok(new { subOrderId = request.SubOrderId, status = "Confirmed", acceptedAt = DateTimeOffset.UtcNow });
    }

    /// <summary>R84: Người bán từ chối đơn hàng con (SubOrderStatus to Cancelled, reason: SellerRejected).</summary>
    [HttpPost("{orderId:guid}/reject")]
    public async Task<IActionResult> SellerRejectOrder(
        [FromRoute] Guid orderId,
        [FromBody] SellerRejectOrderRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var sellerId, out var authError))
        {
            return authError;
        }

        await _orderService.SellerRejectSubOrderAsync(sellerId, orderId, request, ct);
        return Ok(new
        {
            subOrderId = request.SubOrderId,
            status = "Cancelled",
            cancellationReason = request.ReasonCode.ToString(),
            rejectedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>R87: Theo dõi tiến độ giao hàng và thông tin vận chuyển của đơn hàng.</summary>
    [HttpGet("/api/v1/shipments/order/{orderId:guid}")]
    public async Task<IActionResult> GetShipmentTracking([FromRoute] Guid orderId, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var tracking = await _orderService.GetShipmentTrackingAsync(currentUserId, orderId, ct);
        return Ok(tracking);
    }

    /// <summary>Người mua xác nhận hoàn tất toàn bộ đơn hàng (OrderStatus to Completed), tự động kích hoạt thưởng điểm uy tín cho các Seller đối tác.</summary>
    [HttpPost("{orderId:guid}/complete")]
    public async Task<IActionResult> CompleteOrder(
        [FromRoute] Guid orderId,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var response = await _orderService.CompleteOrderAsync(buyerId, orderId, ct);
        return Ok(response);
    }

    /// <summary>Người mua xác nhận đã nhận hàng (SubOrderStatus to Completed), tự động kích hoạt giải ngân Escrow sang ví Seller.</summary>
    [HttpPost("/api/v1/sub-orders/{subOrderId:guid}/confirm-receipt")]
    public async Task<IActionResult> ConfirmSubOrderReceipt(
        [FromRoute] Guid subOrderId,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var response = await _orderService.BuyerConfirmSubOrderReceiptAsync(buyerId, subOrderId, ct);
        return Ok(response);
    }

    /// <summary>R99: Khởi tạo yêu cầu hoàn tiền cho đơn hàng con.</summary>
    [HttpPost("/api/v1/sub-orders/{subOrderId:guid}/refund")]
    public async Task<IActionResult> RefundSubOrder(
        [FromRoute] Guid subOrderId,
        [FromBody] SubOrderRefundRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        await _orderService.RefundSubOrderAsync(currentUserId, subOrderId, request, ct);
        return Ok(new { subOrderId, status = "RefundRequested", requestedAt = DateTimeOffset.UtcNow });
    }

    /// <summary>Webhook callback từ Fulfillment Service cập nhật trạng thái giao hàng của đơn hàng con.</summary>
    [HttpPost("/api/v1/sub-orders/{subOrderId:guid}/delivery-status")]
    public async Task<IActionResult> UpdateDeliveryStatus(
        [FromRoute] Guid subOrderId,
        [FromBody] UpdateDeliveryStatusRequest request,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration,
        CancellationToken ct)
    {
        var expectedKey = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
            ?? configuration["INTERNAL_SERVICE_SECRET"]
            ?? configuration["InternalSecurity:ServiceKey"]
            ?? "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e";

        if (!Request.Headers.TryGetValue("X-Internal-Service-Key", out var providedKey) ||
            !string.Equals(providedKey.ToString(), expectedKey, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                errorCode = "FORBIDDEN",
                message = "Access denied: Missing or invalid X-Internal-Service-Key header."
            });
        }

        await _orderService.UpdateSubOrderDeliveryStatusAsync(subOrderId, request.Status, request.DeliveredAt, ct);
        return Ok(new
        {
            subOrderId,
            status = request.Status,
            updatedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>Tương thích ngược: Tra cứu danh sách đơn của buyer.</summary>
    [HttpGet("buyer/{buyerId:guid}")]
    public async Task<IActionResult> ListByBuyer(
        [FromRoute] Guid buyerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!this.EnsureOwnership(buyerId, out var ownershipError))
        {
            return ownershipError;
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var orders = await _orderService.ListByBuyerIdAsync(buyerId, page, pageSize, ct);
        return Ok(orders);
    }
}
