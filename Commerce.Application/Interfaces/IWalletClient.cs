namespace Commerce.Application.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IWalletClient
{
    Task<WalletAuctionHoldStatus?> GetAuctionDepositHoldAsync(Guid depositId, CancellationToken cancellationToken = default);

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
    /// Tra cứu số dư ví của người dùng:
    /// GET /api/v1/wallets/me
    /// Header: x-user-id: <userId>
    /// </summary>
    Task<decimal> GetWalletBalanceAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a sub-order to the buyer's wallet (RefundCredit).
    /// POST /api/v1/wallets/sub-orders/{subOrderId}/refund
    /// The parent order id is mandatory: the wallet checks the payment and takes the money out of the parent escrow, and refuses the
    /// call without it. The wallet keeps one refund per sub-order id, so there is no idempotency key; a repeated call answers the
    /// existing transaction with <see cref="WalletRefundResult.Replayed"/> set.
    /// </summary>
    Task<WalletRefundResult> RefundSubOrderPaymentAsync(
        Guid subOrderId,
        Guid parentOrderId,
        string userId,
        decimal amount,
        string reason,
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

    /// <summary>
    /// Atomic seller payout (protocol V2): escrows, seller credit, fee and shipping allocation and subsidy use in one Wallet transaction.
    /// POST /api/v1/internal/wallets/order-settlements
    /// </summary>
    Task<WalletCallOutcome> SettleOrderPayoutAsync(WalletSettlementRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reserves the platform-funded voucher subsidy of a sub-order. POST /api/v1/internal/wallets/sub-orders/{subOrderId}/subsidy-hold</summary>
    Task<WalletCallOutcome> HoldPlatformSubsidyAsync(Guid orderId, Guid subOrderId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>Gives a held subsidy back. POST /api/v1/internal/wallets/sub-orders/{subOrderId}/subsidy-release</summary>
    Task<WalletCallOutcome> ReleasePlatformSubsidyAsync(Guid subOrderId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subsidy holds still held after <paramref name="olderThanMinutes"/>, oldest first. Null when Wallet could not answer (never an empty list by mistake).
    /// GET /api/v1/internal/wallets/subsidy-holds/stale
    /// </summary>
    Task<IReadOnlyList<StaleSubsidyHold>?> ListStaleSubsidyHoldsAsync(int olderThanMinutes, int limit, CancellationToken cancellationToken = default);
}

/// <summary>A subsidy reservation that Wallet still holds long after it was taken.</summary>
public sealed record StaleSubsidyHold(Guid SubOrderId, Guid OrderId, decimal Amount, DateTimeOffset HeldSince);

/// <summary>The full allocation of one payout, exactly as stored in the settlement snapshot.</summary>
public sealed record WalletSettlementRequest(
    string BusinessType,
    Guid ParentOrderId,
    Guid SubOrderId,
    Guid SellerId,
    decimal SellerReceiveAmount,
    decimal PlatformFeeAmount,
    decimal ShippingAmount,
    decimal SubsidyAmount,
    decimal OrderEscrowAmount,
    decimal DepositEscrowAmount,
    int PayloadVersion);

/// <summary>
/// Result of a Wallet call that must tell a permanent refusal from a temporary problem. A 4xx answer (conflict, missing money, bad
/// payload) is permanent; a timeout, a 5xx or a disabled feature is transient and worth retrying.
/// </summary>
public sealed record WalletCallOutcome(bool Success, int StatusCode, string? ErrorCode, string? ErrorMessage, bool IsReplay = false)
{
    /// <summary>
    /// Refusals that can clear by themselves: the deposit escrow of an auction order is filled by a background capture in Auction
    /// (Buy Now goods hold), so a payout that arrives first is retried instead of being sent to finance.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> RetryableCodes = new(StringComparer.Ordinal)
    {
        "DEPOSIT_ESCROW_INSUFFICIENT",
        "DEPOSIT_ESCROW_NOT_FOUND"
    };

    public bool IsPermanentFailure => !Success && StatusCode is >= 400 and < 500 and not 408 and not 429
        && (ErrorCode is null || !RetryableCodes.Contains(ErrorCode));
    public bool IsTransientFailure => !Success && !IsPermanentFailure;

    public static WalletCallOutcome Ok(bool isReplay = false) => new(true, 200, null, null, isReplay);
    public static WalletCallOutcome Transient(string? code, string? message) => new(false, 0, code ?? "WALLET_UNAVAILABLE", message);
}

public sealed record WalletAuctionHoldStatus(string Status, string? CapturePurpose, Guid? OrderId);
