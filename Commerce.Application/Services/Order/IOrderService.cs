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

    /// <summary>R76: Tạo đơn hàng chính thức từ mã báo giá quoteId.</summary>
    Task<CreateOrderResponse> CreateOrderAsync(Guid buyerId, CreateOrderRequest request, CancellationToken ct = default);

    /// <summary>R77: Tra cứu thông tin chi tiết đơn hàng cha.</summary>
    Task<OrderResponse> GetOrderDetailAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default);

    /// <summary>R78: Tra cứu lịch sử danh sách đơn hàng có phân trang.</summary>
    Task<PagedData<OrderSummaryResponse>> ListOrdersAsync(Guid currentUserId, int page, int pageSize, OrderStatus? status = null, CancellationToken ct = default);

    /// <summary>R79: Tra cứu thông tin chi tiết đơn hàng con của Shop (Single DTO).</summary>
    Task<SubOrderDetailResponse> GetSubOrderDetailAsync(Guid currentUserId, Guid subOrderId, CancellationToken ct = default);

    /// <summary>R80: Người mua hoặc hệ thống hủy đơn hàng.</summary>
    Task CancelOrderAsync(Guid currentUserId, Guid orderId, CancelOrderRequest request, CancellationToken ct = default);

    /// <summary>R83: Người bán tiếp nhận đơn hàng con.</summary>
    Task SellerAcceptSubOrderAsync(Guid sellerId, Guid orderId, SellerAcceptOrderRequest request, CancellationToken ct = default);

    /// <summary>R84: Người bán từ chối đơn hàng con.</summary>
    Task SellerRejectSubOrderAsync(Guid sellerId, Guid orderId, SellerRejectOrderRequest request, CancellationToken ct = default);

    /// <summary>R87: Theo dõi tiến độ giao hàng và thông tin vận chuyển.</summary>
    Task<ShipmentTrackingResponse> GetShipmentTrackingAsync(Guid currentUserId, Guid orderId, CancellationToken ct = default);

    /// <summary>R99: Khởi tạo yêu cầu hoàn tiền cho đơn con.</summary>
    Task RefundSubOrderAsync(Guid currentUserId, Guid subOrderId, SubOrderRefundRequest request, CancellationToken ct = default);

    /// <summary>Passive Guard: Kiểm tra và hủy đơn hàng nếu đã quá hạn thanh toán.</summary>
    Task<bool> ExpireOrderIfOverdueAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Tương thích ngược: Tra cứu theo Id thô.</summary>
    Task<OrderDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tương thích ngược: Danh sách theo buyerId.</summary>
    Task<PagedData<OrderDto>> ListByBuyerIdAsync(Guid buyerId, int page, int pageSize, CancellationToken cancellationToken = default);
}
