namespace Commerce.Application.Interfaces;

using System;

/// <summary>
/// What the wallet answered to a sub-order refund. <see cref="Replayed"/> is true when the wallet already held a refund for that
/// sub-order and returned it instead of creating a new credit, so the caller can tell a credit it just caused from one that was
/// already there.
/// </summary>
public sealed record WalletRefundResult(bool Succeeded, Guid? TransactionId, bool Replayed, string? ErrorCode)
{
    // No optional parameters: unit tests build these inside Moq expression trees, which do not allow optional arguments.
    public static WalletRefundResult Success() => new(true, null, false, null);

    public static WalletRefundResult Success(Guid? transactionId, bool replayed) => new(true, transactionId, replayed, null);

    public static WalletRefundResult Failure() => new(false, null, false, null);

    public static WalletRefundResult Failure(string? errorCode) => new(false, null, false, errorCode);
}
