namespace Commerce.Domain.Enums;

/// <summary>Where the reputation penalty for a buyer who refused a delivery stands. One per sub-order; User's unique index is the last guard against a double penalty.</summary>
public enum BuyerRefusalPenaltyStatus
{
    /// <summary>No penalty is owed: the return was not classified as a refusal, or the rules were not enabled when the parcel came back.</summary>
    None = 0,
    /// <summary>Decided and owed, not yet confirmed by User. The worker keeps trying.</summary>
    Pending = 1,
    /// <summary>User confirmed it (applied, already applied, or suppressed by its own rules).</summary>
    Applied = 2
}
