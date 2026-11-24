namespace Commerce.Domain.Enums;

public enum RefundStatus
{
    Requested = 0,
    Completed = 1,
    Failed = 2,
    Rejected = 3,

    /// <summary>
    /// The refund has been decided (approved, or required by a cancellation, a return or a dispute) and is waiting for the wallet to
    /// confirm it. <see cref="Requested"/> now only means "waiting for a decision"; only this status is retried by the workers.
    /// </summary>
    WalletPending = 4
}

