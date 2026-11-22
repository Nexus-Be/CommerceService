namespace Commerce.Application.Interfaces;

/// <summary>
/// Which carrier reason codes mean "the buyer refused the parcel", and how much reputation that costs. Both come from configuration and are
/// empty/zero until the project owner approves a table (DeliveryFailure:BuyerRefusedReasonCodes, DeliveryFailure:BuyerRefusalScoreDelta).
/// While either is missing no buyer is penalised; the evidence is still kept.
/// </summary>
public interface IDeliveryFailurePolicy
{
    /// <summary>Version label of the approved table, written to the log when a penalty is decided.</summary>
    string TableVersion { get; }

    /// <summary>True only when at least one reason code is approved and the penalty is a negative number.</summary>
    bool Enabled { get; }

    bool IsBuyerRefusedCode(string? reasonCode);

    /// <summary>A negative reputation change (never positive, never zero when <see cref="Enabled"/>).</summary>
    int BuyerRefusalScoreDelta { get; }
}
