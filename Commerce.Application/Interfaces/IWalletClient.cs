namespace Commerce.Application.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IWalletClient
{
    /// <summary>
    /// Thanh toán đơn hàng qua endpoint chuẩn của WalletService:
    /// POST /api/v1/wallets/orders/{orderId}/payment
    /// </summary>
    Task<WalletPaymentResult> PayOrderAsync(
        Guid orderId,
        decimal amount,
        string? bearerToken = null,
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

    Task<decimal> GetWalletBalanceAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tự động hoàn tiền SubOrder vào ví Buyer qua WalletService (RefundCredit).
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
    /// POST api/v1/wallets/sub-orders/{subOrderId}/settle
    /// </summary>
    Task<bool> SettleSellerSubOrderAsync(
        Guid subOrderId,
        Guid sellerId,
        decimal amount,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
}
