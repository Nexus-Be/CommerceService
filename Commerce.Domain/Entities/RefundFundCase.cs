using Commerce.Domain.Base;
using Commerce.Domain.Enums;

namespace Commerce.Domain.Entities;

/// <summary>
/// The decision to refund a buyer from the refund fund after the sub-order was already paid out to the seller. One row per sub-order (unique):
/// two reports about the same sub-order reach the same row, so only one refund can exist. The decision and the approvals are stored before the
/// Wallet is called; the sub-order is shown as refunded only after the Wallet confirms.
/// </summary>
public class RefundFundCase : BaseEntity<Guid>
{
    public Guid SubOrderId { get; set; }
    public Guid ParentOrderId { get; set; }
    public Guid BuyerId { get; set; }
    public Guid SellerId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>The first report that led to the decision, kept only to trace it; it is not part of the key.</summary>
    public Guid? ReportId { get; set; }
    public string RequestReason { get; set; } = string.Empty;
    public RefundFundCaseStatus Status { get; set; } = RefundFundCaseStatus.Pending;
    public Guid FirstApproverId { get; set; }
    public Guid? SecondApproverId { get; set; }
    /// <summary>Why the fund did not pay (a Wallet answer or error code), for a case in NeedsFinance.</summary>
    public string? NeedsFinanceReason { get; set; }
    public decimal? SellerRecovered { get; set; }
    public decimal? SellerShortfall { get; set; }
    public Guid? BuyerTransactionId { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
