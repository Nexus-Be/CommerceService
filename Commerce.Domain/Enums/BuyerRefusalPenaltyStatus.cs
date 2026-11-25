namespace Commerce.Domain.Enums;

/// <summary>Where the reputation penalty for a buyer who refused a delivery stands. One per sub-order; User's unique index is the last guard against a double penalty.</summary>
public enum BuyerRefusalPenaltyStatus
{
    /// <summary>No penalty is owed: the return was not classified as a refusal, or the rules were not enabled when the parcel came back.</summary>
    None = 0,
    /// <summary>Decided and owed, not yet confirmed by User. The worker keeps trying.</summary>
    Pending = 1,
    /// <summary>User confirmed it (applied, already applied, or suppressed by its own rules).</summary>
    Applied = 2,
    /// <summary>An admin withdrew the penalty. User has not confirmed yet (or Commerce could not reach it); the worker repeats the call until it does. Nothing is lost whichever side was ahead.</summary>
    RevokePending = 4,
    /// <summary>The withdrawal is confirmed and the points the penalty really took were given back.</summary>
    Revoked = 5,
    /// <summary>The withdrawal is confirmed and the penalty had never taken any points (it had not arrived at User, or the score was already at its floor).</summary>
    Cancelled = 3
}
