namespace Commerce.Application.Services.Order;

using System;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Common.Models;
using Commerce.Domain.Enums;

public interface IOrderService
{
    /// <summary>R75: Tạo phiên báo giá thanh toán xem trước (tính tiền ship GHN, voucher, 15m TTL).</summary>
    Task<CheckoutQuoteResponse> QuoteCheckoutAsync(Guid buyerId, CheckoutQuoteRequest request, CancellationToken ct = default);

    /// <summary>Tính lại phí vận chuyển GHN và tổng thanh toán khi đổi địa chỉ ở màn Checkout.</summary>
    Task<CheckoutQuoteResponse> RecalculateQuoteAsync(Guid buyerId, Guid quoteId, RecalculateQuoteRequest request, CancellationToken ct = default);

    /// <summary>Xác minh đơn hàng hợp lệ phục vụ chức năng Khiếu nại/Tố cáo đối tác cho UserService.</summary>
    Task<DisputeEligibilityDto?> GetDisputeEligibilityAsync(Guid orderOrSubOrderId, Guid reporterId, Guid reportedUserId, CancellationToken ct = default);

    /// <summary>R76: Tạo đơn hàng chính thức từ mã báo giá quoteId.</summary>
    Task<CreateOrderResponse> CreateOrderAsync(Guid buyerId, CreateOrderRequest request, CancellationToken ct = default);

    /// <summary>R77: Tra cứu thông tin chi tiết đơn hàng cha.</summary>
    Task<OrderResponse> GetOrderDetailAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default);
    Task<OrderResponse> GetOrderDetailAsync(Guid currentUserId, Guid orderId, bool isAdminOrStaff, CancellationToken ct = default);

    /// <summary>R78: Tra cứu lịch sử danh sách đơn hàng có phân trang.</summary>
    Task<PagedData<OrderSummaryResponse>> ListOrdersAsync(Guid currentUserId, int page, int pageSize, OrderStatus? status = null, CancellationToken ct = default);

    /// <summary>R79: Tra cứu thông tin chi tiết đơn hàng con của Shop (Single DTO).</summary>
    Task<SubOrderDetailResponse> GetSubOrderDetailAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct = default);
    Task<SubOrderDetailResponse> GetSubOrderDetailAsync(Guid currentUserId, Guid subOrderId, bool isAdminOrStaff, CancellationToken ct = default);

    /// <summary>R80: Người mua hoặc hệ thống hủy đơn hàng.</summary>
    Task CancelOrderAsync(Guid currentUserId, Guid orderId, CancelOrderRequest request, CancellationToken ct = default);

    /// <summary>Người mua hủy riêng một đơn hàng con (SubOrder) của Shop cụ thể trước khi hàng được gửi đi.</summary>
    Task<CancelSubOrderResponse> CancelSubOrderAsync(Guid currentUserId, Guid subOrderId, CancelSubOrderRequest request, CancellationToken ct = default);

    /// <summary>R83: Người bán tiếp nhận đơn hàng con.</summary>
    Task SellerAcceptSubOrderAsync(Guid sellerId, Guid orderId, SellerAcceptOrderRequest request, CancellationToken ct = default);

    /// <summary>R84: Người bán từ chối đơn hàng con.</summary>
    Task SellerRejectSubOrderAsync(Guid sellerId, Guid orderId, SellerRejectOrderRequest request, CancellationToken ct = default);

    /// <summary>Người mua xác nhận đã nhận hàng (SubOrderStatus -> Completed), kích hoạt giải ngân sang ví Seller.</summary>
    Task<ConfirmReceiptResponse> BuyerConfirmSubOrderReceiptAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct = default);

    /// <summary>Người mua xác nhận hoàn tất toàn bộ đơn hàng (OrderStatus -> Completed), kích hoạt thưởng điểm uy tín cho các Seller đối tác.</summary>
    Task<OrderCompleteResponse> CompleteOrderAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default);

    /// <summary>Người bán tra cứu danh sách đơn hàng con thuộc Shop của mình, có phân trang database và lọc status.</summary>
    Task<PagedData<SubOrderSummaryResponse>> ListSellerSubOrdersAsync(Guid sellerId, int page, int pageSize, SubOrderStatus? status = null, CancellationToken ct = default);

    /// <summary>R87: Theo dõi tiến độ giao hàng và thông tin vận chuyển.</summary>
    Task<ShipmentTrackingResponse> GetShipmentTrackingAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default);
    Task<ShipmentTrackingResponse> GetShipmentTrackingAsync(Guid currentUserId, Guid orderId, bool isAdminOrStaff, CancellationToken ct = default);

    /// <summary>Theo dõi dòng thời gian hành trình giao hàng chi tiết cho đơn con.</summary>
    Task<ShipmentTimelineResponse> GetShipmentTimelineAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct = default);
    Task<ShipmentTimelineResponse> GetShipmentTimelineAsync(Guid currentUserId, Guid subOrderId, bool isAdminOrStaff, CancellationToken ct = default);

    /// <summary>Tạo lại vận đơn giao hàng khi đơn con đã xác nhận nhưng gặp sự cố mạng tạm thời với GHN.</summary>
    Task<RetryShipmentResponse> RetryCreateShipmentAsync(Guid sellerId, Guid subOrderId, CancellationToken ct = default);

    /// <summary>R99: Khởi tạo yêu cầu hoàn tiền cho đơn con.</summary>
    Task RefundSubOrderAsync(Guid currentUserId, Guid subOrderId, SubOrderRefundRequest request, CancellationToken ct = default);

    /// <summary>Cập nhật trạng thái giao hàng của SubOrder từ Webhook/Fulfillment Service.</summary>
    Task UpdateSubOrderDeliveryStatusAsync(Guid subOrderId, string status, DateTimeOffset? deliveredAt, CancellationToken ct = default);

    /// <summary>Cập nhật trạng thái giao hàng của SubOrder từ Webhook/Fulfillment Service với đầy đủ chi tiết sự kiện.</summary>
    Task UpdateSubOrderDeliveryStatusAsync(Guid subOrderId, UpdateDeliveryStatusRequest request, CancellationToken ct = default);

    /// <summary>
    /// For UserService: the buyer, seller, status and completion time of a sub-order (ReferenceType Order) or an auction order
    /// (ReferenceType Auction). Returns null when no such order exists.
    /// </summary>
    Task<ReviewEligibilityDto?> GetReviewEligibilityAsync(string referenceType, Guid referenceId, CancellationToken ct = default);

    /// <summary>Passive Guard: Kiểm tra và hủy đơn hàng nếu đã quá hạn thanh toán.</summary>
    Task<bool> ExpireOrderIfOverdueAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Thanh toán lại đơn hàng đang ở trạng thái PendingPayment trong thời hạn 15 phút.</summary>
    Task<RepayOrderResponse> RepayOrderAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default);

    /// <summary>Tra cứu lịch sử hoàn tiền chi tiết của đơn hàng.</summary>
    Task<List<OrderRefundResponse>> GetOrderRefundsAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default);

    /// <summary>Tra cứu bảng kê phân rã tài chính, doanh thu thực nhận và phí sàn của đơn hàng con cho Seller.</summary>
    Task<SubOrderFinancialBreakdownResponse> GetSubOrderFinancialBreakdownAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct = default);

    /// <summary>Tương thích ngược: Tra cứu theo Id thô.</summary>
    Task<OrderDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tương thích ngược: Danh sách theo buyerId.</summary>
    Task<PagedData<OrderDto>> ListByBuyerIdAsync(Guid buyerId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Đổi địa chỉ nhận hàng của đơn hàng con sau khi đặt hàng.</summary>
    Task<ChangeShippingAddressResponse> ChangeSubOrderShippingAddressAsync(Guid currentUserId, Guid subOrderId, ChangeShippingAddressRequest request, bool isAdminOrStaff = false, CancellationToken ct = default);

    /// <summary>Đổi địa chỉ nhận hàng của toàn bộ đơn hàng cha sau khi đặt hàng.</summary>
    Task<ChangeShippingAddressResponse> ChangeOrderShippingAddressAsync(Guid currentUserId, Guid orderId, ChangeShippingAddressRequest request, bool isAdminOrStaff = false, CancellationToken ct = default);

    /// <summary>Tra cứu danh sách yêu cầu hoàn tiền có phân trang database.</summary>
    Task<PagedData<RefundDetailResponse>> ListRefundsAsync(Guid currentUserId, int page, int pageSize, RefundStatus? status = null, Guid? subOrderId = null, bool isAdminOrStaff = false, CancellationToken ct = default);

    /// <summary>Tra cứu chi tiết yêu cầu hoàn tiền theo ID.</summary>
    Task<RefundDetailResponse> GetRefundDetailAsync(Guid currentUserId, Guid refundId, bool isAdminOrStaff = false, CancellationToken ct = default);

    /// <summary>Duyệt yêu cầu hoàn tiền và hoàn tiền ví cho người mua.</summary>
    /// <summary>
    /// Refunds a delivered, wallet-paid sub-order to the buyer after a dispute was decided in the buyer's favour. Idempotent per report:
    /// a second call for the same report returns the first result. A sub-order whose money already went to the seller, or whose parent order
    /// was refunded separately, is not refunded automatically and is reported as ManualProcessingRequired.
    /// </summary>
    Task<DisputeRefundResponse> RefundSubOrderForDisputeAsync(Guid subOrderId, Guid reportId, string? note, CancellationToken ct = default);

    Task<RefundDetailResponse> ApproveRefundAsync(Guid currentUserId, Guid refundId, ApproveRefundRequest request, bool isAdminOrStaff = false, CancellationToken ct = default);

    /// <summary>Từ chối yêu cầu hoàn tiền kèm lý do.</summary>
    Task<RefundDetailResponse> RejectRefundAsync(Guid currentUserId, Guid refundId, RejectRefundRequest request, bool isAdminOrStaff = false, CancellationToken ct = default);

    /// <summary>Admin/Staff: Tra cứu danh sách đơn hàng cha toàn sàn.</summary>
    Task<PagedData<OrderSummaryResponse>> ListAdminOrdersAsync(AdminOrderFilterRequest filter, CancellationToken ct = default);

    /// <summary>Admin/Staff: Tra cứu danh sách đơn hàng con toàn sàn.</summary>
    Task<PagedData<SubOrderSummaryResponse>> ListAdminSubOrdersAsync(AdminSubOrderFilterRequest filter, CancellationToken ct = default);
}
