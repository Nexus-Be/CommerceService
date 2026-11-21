namespace Commerce.Application.Interfaces;

using Commerce.Domain.Entities;

/// <summary>
/// Seller payout for completed orders, protocol V2: the payout payload is captured once, committed, and only then sent to Wallet
/// as one atomic command. Retries always send the stored payload. Orders still on protocol V1 keep using the legacy payout route
/// until it is switched off; after that they are upgraded here and Wallet refuses to pay anything V1 already paid.
/// </summary>
public interface ISettlementService
{
    /// <summary>Protocol assigned to orders created now (configuration <c>Settlement:ProtocolVersion</c>, default 1).</summary>
    int NewOrderProtocolVersion { get; }

    /// <summary>
    /// Pays the seller of a completed sub-order. Returns true only when Wallet confirmed the payout (also when it was already done
    /// with the same payload). Never throws for a payout problem: the snapshot records it and the retry worker or finance handles it.
    /// </summary>
    Task<bool> SettleSubOrderAsync(SubOrder subOrder, CancellationToken cancellationToken, bool respectBackoff = false);

    /// <summary>Pays the seller of a completed auction order using both the deposit escrow and the order escrow. Same contract as <see cref="SettleSubOrderAsync"/>.</summary>
    Task<bool> SettleAuctionOrderAsync(AuctionOrder order, CancellationToken cancellationToken, bool respectBackoff = false);

    /// <summary>Reserves the platform-funded voucher subsidy of a sub-order. Throws VOUCHER_BUDGET_EXHAUSTED (409) or SUBSIDY_UNAVAILABLE (503).</summary>
    Task HoldSubsidyAsync(Guid orderId, Guid subOrderId, decimal amount, CancellationToken cancellationToken);

    /// <summary>Best-effort release of a held subsidy. Returns true when Wallet confirmed (or there was nothing to release).</summary>
    Task<bool> ReleaseSubsidyAsync(Guid subOrderId, string reason, CancellationToken cancellationToken);

    /// <summary>One worker pass: retries due payouts, finishes the order flags, and releases subsidies of cancelled sub-orders. Returns how many payouts completed.</summary>
    Task<int> RunPendingWorkAsync(int batchSize, CancellationToken cancellationToken);
}
