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
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Commerce.Api.Security;

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

    /// <summary>Tính lại phí vận chuyển GHN và tổng thanh toán khi đổi địa chỉ ở màn Checkout.</summary>
    [HttpPost("/api/v1/checkouts/quote/{quoteId:guid}/recalculate")]
    public async Task<IActionResult> RecalculateQuote(
        [FromRoute] Guid quoteId,
        [FromBody] RecalculateQuoteRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var response = await _orderService.RecalculateQuoteAsync(buyerId, quoteId, request, ct);
        return Ok(response);
    }

    /// <summary>Xác minh đơn hàng hợp lệ phục vụ chức năng Khiếu nại/Tố cáo đối tác cho UserService.</summary>
    [HttpGet("/api/v1/internal/orders/dispute-eligibility")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDisputeEligibility(
        [FromQuery] Guid orderOrSubOrderId,
        [FromQuery] Guid reporterId,
        [FromQuery] Guid reportedUserId,
        [FromServices] IConfiguration configuration,
        CancellationToken ct)
    {
        var authError = InternalServiceKeyGuard.Check(Request, configuration);
        if (authError != null)
        {
            return authError;
        }

        if (orderOrSubOrderId == Guid.Empty || reporterId == Guid.Empty || reportedUserId == Guid.Empty)
        {
            return BadRequest(new
            {
                errorCode = "VAL_INVALID_INPUT",
                message = "Order/SubOrder ID, reporter ID, and reported user ID are required."
            });
        }

        var eligibility = await _orderService.GetDisputeEligibilityAsync(orderOrSubOrderId, reporterId, reportedUserId, ct);
        if (eligibility == null)
        {
            return NotFound(new
            {
                errorCode = "ORDER_NOT_FOUND",
                message = "Order or sub-order was not found or users do not match."
            });
        }

        return Ok(eligibility);
    }

    /// <summary>Thực hiện giải ngân hoàn tiền ví và chuyển SubOrder sang Refunded khi giải quyết khiếu nại thành công (Internal S2S cho UserService).</summary>
    [HttpPost("/api/v1/internal/sub-orders/{subOrderId:guid}/dispute-refund")]
    [AllowAnonymous]
    public async Task<IActionResult> DisputeRefundSubOrder(
        [FromRoute] Guid subOrderId,
        [FromBody] DisputeRefundRequest request,
        [FromServices] IConfiguration configuration,
        CancellationToken ct)
    {
        var denied = InternalServiceKeyGuard.Check(Request, configuration);
        if (denied is not null)
        {
            return denied;
        }

        var result = await _orderService.DisputeRefundSubOrderAsync(subOrderId, request, ct);
        return Ok(result);
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

        var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
        var order = await _orderService.GetOrderDetailAsync(currentUserId, orderId, isAdminOrStaff, ct);
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

        var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
        var subOrder = await _orderService.GetSubOrderDetailAsync(currentUserId, subOrderId, isAdminOrStaff, ct);
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
    [HttpGet("{orderId:guid}/shipments")]
    [HttpGet("/api/v1/shipments/order/{orderId:guid}", Order = 2)]
    public async Task<IActionResult> GetShipmentTracking([FromRoute] Guid orderId, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
        var tracking = await _orderService.GetShipmentTrackingAsync(currentUserId, orderId, isAdminOrStaff, ct);
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

    /// <summary>R102: Người mua hủy riêng một đơn hàng con trước khi giao hàng (SubOrderStatus to Cancelled).</summary>
    [HttpPost("/api/v1/sub-orders/{subOrderId:guid}/cancel")]
    public async Task<IActionResult> CancelSubOrder(
        [FromRoute] Guid subOrderId,
        [FromBody] CancelSubOrderRequest? request = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var effectiveRequest = request ?? new CancelSubOrderRequest(CancellationReasonCode.BuyerChangedMind, "Buyer cancelled sub-order.");
        var response = await _orderService.CancelSubOrderAsync(currentUserId, subOrderId, effectiveRequest, ct);
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

    /// <summary>
    /// Called by UserService before it accepts a review: who bought, who sold, and whether and when the order finished.
    /// ReferenceType Order is a sub-order ID (not the parent order); Auction is an auction order ID. 404 when there is no such order.
    /// </summary>
    [HttpGet("/api/v1/internal/review-eligibility")]
    // Service-to-service: no user, so the role check is skipped and the internal service key decides.
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> GetReviewEligibility(
        [FromQuery] string referenceType,
        [FromQuery] Guid referenceId,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration,
        CancellationToken ct)
    {
        var denied = Commerce.Api.Security.InternalServiceKeyGuard.Check(Request, configuration);
        if (denied is not null)
        {
            return denied;
        }

        if (!ReviewReferenceTypes.TryNormalize(referenceType, out _) || referenceId == Guid.Empty)
        {
            return BadRequest(new { errorCode = "VAL_INVALID_INPUT", message = "Reference type must be Order or Auction and the reference ID must be a valid UUID." });
        }

        var result = await _orderService.GetReviewEligibilityAsync(referenceType, referenceId, ct);
        return result is null
            ? NotFound(new { errorCode = "REVIEW_ORDER_NOT_FOUND", message = "The order was not found." })
            : Ok(result);
    }

    /// <summary>Webhook callback từ Fulfillment Service cập nhật trạng thái giao hàng của đơn hàng con.</summary>
    [HttpPost("/api/v1/sub-orders/{subOrderId:guid}/delivery-status")]
    // Called by Fulfillment, which has no user: the class-level role check is skipped and the internal service key is checked below.
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> UpdateDeliveryStatus(
        [FromRoute] Guid subOrderId,
        [FromBody] UpdateDeliveryStatusRequest request,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration,
        CancellationToken ct)
    {
        var denied = Commerce.Api.Security.InternalServiceKeyGuard.Check(Request, configuration);
        if (denied is not null)
        {
            return denied;
        }

        await _orderService.UpdateSubOrderDeliveryStatusAsync(subOrderId, request, ct);
        return Ok(new
        {
            subOrderId,
            status = request.Status,
            updatedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Called by User after a dispute is decided for the buyer. The caller sends only the report ID; Commerce works out the amount
    /// and refuses to refund twice for the same report.
    /// </summary>
    [HttpPost("/api/v1/sub-orders/{subOrderId:guid}/dispute-refund")]
    // Called service to service: the class-level role check is skipped and the internal service key is checked below.
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> RefundForDispute(
        [FromRoute] Guid subOrderId,
        [FromBody] SubOrderDisputeRefundRequest request,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration,
        CancellationToken ct)
    {
        var denied = Commerce.Api.Security.InternalServiceKeyGuard.Check(Request, configuration);
        if (denied is not null)
        {
            return denied;
        }

        return Ok(await _orderService.RefundSubOrderForDisputeAsync(subOrderId, request.ReportId, request.Note, ct));
    }

    /// <summary>R100: Tạo lại vận đơn giao hàng khi đơn con đã xác nhận nhưng gặp sự cố mạng tạm thời với GHN.</summary>
    [HttpPost("/api/v1/sub-orders/{subOrderId:guid}/shipments/retry")]
    public async Task<IActionResult> RetryCreateShipment(
        [FromRoute] Guid subOrderId,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var sellerId, out var authError))
        {
            return authError;
        }

        var response = await _orderService.RetryCreateShipmentAsync(sellerId, subOrderId, ct);
        return Ok(response);
    }

    /// <summary>R101: Theo dõi dòng thời gian hành trình giao hàng chi tiết cho đơn con.</summary>
    [HttpGet("/api/v1/sub-orders/{subOrderId:guid}/shipments/timeline")]
    public async Task<IActionResult> GetShipmentTimeline(
        [FromRoute] Guid subOrderId,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
        var timeline = await _orderService.GetShipmentTimelineAsync(currentUserId, subOrderId, isAdminOrStaff, ct);
        return Ok(timeline);
    }

    /// <summary>Thanh toán lại đơn hàng đang ở trạng thái PendingPayment trong thời hạn 15 phút.</summary>
    [HttpPost("{orderId:guid}/pay")]
    public async Task<IActionResult> RepayOrder(
        [FromRoute] Guid orderId,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var response = await _orderService.RepayOrderAsync(currentUserId, orderId, ct);
        return Ok(response);
    }

    /// <summary>Tra cứu lịch sử hoàn tiền chi tiết của đơn hàng.</summary>
    [HttpGet("{orderId:guid}/refunds")]
    public async Task<IActionResult> GetOrderRefunds(
        [FromRoute] Guid orderId,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var response = await _orderService.GetOrderRefundsAsync(currentUserId, orderId, ct);
        return Ok(response);
    }

    /// <summary>Tra cứu bảng kê phân rã tài chính, doanh thu thực nhận và phí sàn của đơn hàng con cho Seller.</summary>
    [HttpGet("/api/v1/sub-orders/{subOrderId:guid}/financial-breakdown")]
    public async Task<IActionResult> GetSubOrderFinancialBreakdown(
        [FromRoute] Guid subOrderId,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var response = await _orderService.GetSubOrderFinancialBreakdownAsync(currentUserId, subOrderId, ct);
        return Ok(response);
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

    /// <summary>Đổi địa chỉ nhận hàng của đơn hàng con sau khi đặt hàng.</summary>
    [HttpPatch("/api/v1/sub-orders/{subOrderId:guid}/shipping-address")]
    [RequireRole("buyer", "admin", "staff")]
    public async Task<IActionResult> ChangeSubOrderShippingAddress(
        [FromRoute] Guid subOrderId,
        [FromBody] ChangeShippingAddressRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
        var response = await _orderService.ChangeSubOrderShippingAddressAsync(currentUserId, subOrderId, request, isAdminOrStaff, ct);
        return Ok(response);
    }

    /// <summary>Đổi địa chỉ nhận hàng của toàn bộ đơn hàng cha sau khi đặt hàng.</summary>
    [HttpPatch("{orderId:guid}/shipping-address")]
    [RequireRole("buyer", "admin", "staff")]
    public async Task<IActionResult> ChangeOrderShippingAddress(
        [FromRoute] Guid orderId,
        [FromBody] ChangeShippingAddressRequest request,
        CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var isAdminOrStaff = this.IsAdmin() || this.HasRole("staff");
        var response = await _orderService.ChangeOrderShippingAddressAsync(currentUserId, orderId, request, isAdminOrStaff, ct);
        return Ok(response);
    }

    /// <summary>Admin/Staff: Tra cứu danh sách đơn hàng cha toàn sàn.</summary>
    [HttpGet("/api/v1/admin/orders")]
    [RequireRole("admin", "staff")]
    public async Task<IActionResult> ListAdminOrders(
        [FromQuery] AdminOrderFilterRequest filter,
        CancellationToken ct)
    {
        var response = await _orderService.ListAdminOrdersAsync(filter, ct);
        return Ok(response);
    }

    /// <summary>Admin/Staff: Tra cứu danh sách đơn hàng con toàn sàn.</summary>
    [HttpGet("/api/v1/admin/sub-orders")]
    [RequireRole("admin", "staff")]
    public async Task<IActionResult> ListAdminSubOrders(
        [FromQuery] AdminSubOrderFilterRequest filter,
        CancellationToken ct)
    {
        var response = await _orderService.ListAdminSubOrdersAsync(filter, ct);
        return Ok(response);
    }
}
