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

public sealed record DirectCheckoutResult(
    bool IsSuccess,
    string? PaymentUrl,
    Guid? TransactionId,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static DirectCheckoutResult Success(string paymentUrl, Guid? transactionId) =>
        new(true, PaymentUrl: paymentUrl, TransactionId: transactionId);

    public static DirectCheckoutResult Failure(string errorCode, string errorMessage) =>
        new(false, PaymentUrl: null, TransactionId: null, ErrorCode: errorCode, ErrorMessage: errorMessage);
}

public sealed record DirectCheckoutStatusResult(
    Guid OrderId,
    Guid? TransactionId,
    string Status,
    decimal Amount,
    DateTimeOffset? PaidAt);
