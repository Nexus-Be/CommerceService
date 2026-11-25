namespace Commerce.Domain.Enums;

/// <summary>Reserve, release and use of the platform-funded part of a voucher discount, one state per sub-order.</summary>
public enum SubsidyState
{
    /// <summary>No subsidy reserved (no platform voucher, COD, or an order created before the budget existed).</summary>
    None = 0,
    /// <summary>Reserved in Wallet when the order was created.</summary>
    Held = 1,
    /// <summary>Given back to the budget because the sub-order was cancelled or refunded before payout.</summary>
    Released = 2,
    /// <summary>Paid to the seller by the payout.</summary>
    Used = 3
}
