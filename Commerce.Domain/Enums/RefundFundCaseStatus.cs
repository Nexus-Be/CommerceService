namespace Commerce.Domain.Enums;

public enum RefundFundCaseStatus
{
    /// <summary>The amount is above the threshold: a second admin, different from the first, must approve before the fund is asked.</summary>
    AwaitingSecondApproval = 0,
    /// <summary>Decided and approved; the Wallet has not confirmed yet. The worker repeats the request until it does.</summary>
    Pending = 1,
    /// <summary>The Wallet moved the money. Only now is the buyer shown as refunded.</summary>
    Refunded = 2,
    /// <summary>The fund could not pay (empty, over a cap, already refunded another way...). A person handles it; nothing moved.</summary>
    NeedsFinance = 3,
    /// <summary>A person closed a NeedsFinance case after handling it outside the system.</summary>
    Resolved = 4
}
