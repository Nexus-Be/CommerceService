using Commerce.Domain.Base;

namespace Commerce.Domain.Entities;

public enum SettlementSnapshotStatus
{
    /// <summary>Captured, not yet confirmed by Wallet. The retry worker picks it up when NextRetryAt has passed.</summary>
    Pending = 0,
    /// <summary>Wallet confirmed the payout (or confirmed it had already been done with the same payload).</summary>
    Completed = 1,
    /// <summary>A permanent problem (conflict, missing source money, broken identity, too many attempts). Needs a finance decision; it is never retried or paid automatically.</summary>
    NeedsFinance = 2
}

/// <summary>
/// The exact payout payload of one sub-order (or auction order), captured once when it becomes payable and committed before Wallet is called.
/// Every retry sends this stored payload unchanged, so later changes to fee rate, voucher or prices cannot alter a payout that was already
/// attempted. It also carries the retry state of the payout. Unique per (BusinessType, SubOrderId).
/// </summary>
public class SettlementSnapshot : BaseEntity<Guid>
{
    public required string BusinessType { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid SubOrderId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal SellerReceiveAmount { get; init; }
    public required decimal PlatformFeeAmount { get; init; }
    public required decimal ShippingAmount { get; init; }
    public required decimal SubsidyAmount { get; init; }
    public required decimal OrderEscrowAmount { get; init; }
    public required decimal DepositEscrowAmount { get; init; }
    public int PayloadVersion { get; init; } = 1;
    public SettlementSnapshotStatus Status { get; set; } = SettlementSnapshotStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
