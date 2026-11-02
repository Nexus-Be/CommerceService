namespace Commerce.Application.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IWalletClient
{
    /// <summary>
    /// Thanh toán đơn hàng qua endpoint nội bộ chuẩn của WalletService:
    /// POST /api/v1/internal/wallet/orders/{orderId}/payment
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

    Task<bool> RefundSubOrderPaymentAsync(
        Guid subOrderId,
        string userId,
        decimal amount,
        string reason,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<bool> RefundOrderPaymentAsync(
        Guid orderId,
        string userId,
        decimal amount,
        string reason,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
}
