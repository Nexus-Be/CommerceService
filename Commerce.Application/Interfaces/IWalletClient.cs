namespace Commerce.Application.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IWalletClient
{
    /// <summary>
    /// Thanh toán đơn hàng qua endpoint chuẩn của WalletService:
    /// POST /api/v1/wallets/orders/{orderId}/payment
    /// Header: x-user-id: <buyerId>
    /// </summary>
    Task<WalletPaymentResult> PayOrderAsync(
        Guid orderId,
        Guid buyerId,
        decimal amount,
        string? bearerToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quá tải hỗ trợ tương thích ngược khi không truyền buyerId (lấy từ HttpContext).
    /// </summary>
    Task<WalletPaymentResult> PayOrderAsync(
        Guid orderId,
        decimal amount,
        string? bearerToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Khởi tạo phiên thanh toán trực tiếp qua cổng VNPay:
    /// POST /api/v1/wallets/checkout/direct
    /// Header: x-user-id: <buyerId>
    /// </summary>
    Task<DirectCheckoutResult> InitiateDirectCheckoutAsync(
        Guid orderId,
        Guid buyerId,
        decimal amount,
        string? returnUrl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thăm dò trạng thái quyết toán thanh toán trực tiếp VNPay cho đơn hàng:
    /// GET /api/v1/wallets/checkout/direct/{orderId}
    /// Header: x-user-id: <buyerId>
    /// </summary>
    Task<DirectCheckoutStatusResult?> GetDirectCheckoutStatusAsync(
        Guid orderId,
        Guid buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Phương thức tương thích ngược.
    /// </summary>
    Task<bool> CheckoutDirectPaymentAsync(
        Guid orderId,
        string userId,
        decimal amount,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tra cứu số dư ví của người dùng:
    /// GET /api/v1/wallets/me
    /// Header: x-user-id: <userId>
    /// </summary>
    Task<decimal> GetWalletBalanceAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tự động hoàn tiền SubOrder vào ví Buyer qua WalletService (RefundCredit).
    /// POST /api/v1/wallets/sub-orders/{subOrderId}/refund
    /// </summary>
    Task<bool> RefundSubOrderPaymentAsync(
        Guid subOrderId,
        string userId,
        decimal amount,
        string reason,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tự động hoàn tiền Master Order vào ví Buyer qua WalletService (RefundCredit).
    /// POST /api/v1/wallets/orders/{orderId}/refund
    /// </summary>
    Task<bool> RefundOrderPaymentAsync(
        Guid orderId,
        string userId,
        decimal amount,
        string reason,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Giải ngân tiền từ Escrow sang ví của Seller khi Buyer xác nhận đã nhận hàng (EscrowRelease).
    /// POST /api/v1/wallets/sub-orders/{subOrderId}/settle
    /// </summary>
    Task<bool> SettleSellerSubOrderAsync(
        Guid subOrderId,
        Guid sellerId,
        decimal amount,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
}
