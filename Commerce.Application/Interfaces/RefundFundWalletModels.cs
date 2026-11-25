namespace Commerce.Application.Interfaces;

public sealed record RefundFundPayRequest(
    Guid SubOrderId,
    Guid ParentOrderId,
    Guid BuyerId,
    decimal Amount,
    Guid FirstApproverId,
    Guid? SecondApproverId);

/// <summary>
/// Outcome is "Refunded" (money moved, or an earlier identical request is repeated), "NeedsFinance" (the fund could not pay; Reason says why) or
/// "Rejected" (the Wallet refused the request for a reason that will not change by itself; Reason is its error code).
/// </summary>
public sealed record RefundFundPayResponse(
    string Outcome,
    string? Reason,
    decimal Amount,
    decimal SellerRecovered,
    decimal SellerShortfall,
    bool Replayed,
    string? BuyerTransactionId);
