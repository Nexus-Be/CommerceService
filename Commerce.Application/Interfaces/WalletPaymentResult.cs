namespace Commerce.Application.Interfaces;

using System;

public sealed record WalletPaymentResult(
    bool IsSuccess,
    string? TransactionId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool IsTransientFailure = false,
    decimal? BalanceAfter = null)
{
    public static WalletPaymentResult Success(string transactionId, decimal? balanceAfter = null) =>
        new(true, TransactionId: transactionId, BalanceAfter: balanceAfter);

    public static WalletPaymentResult Failure(string errorCode, string errorMessage, bool isTransient = false) =>
        new(false, ErrorCode: errorCode, ErrorMessage: errorMessage, IsTransientFailure: isTransient);

    public static WalletPaymentResult Transient(string errorCode, string errorMessage) =>
        new(false, ErrorCode: errorCode, ErrorMessage: errorMessage, IsTransientFailure: true);
}
