namespace Commerce.Domain.Enums;

public static class AuctionOrderSourceExtensions
{
    /// <summary>
    /// Value used in the contract with AuctionService: WINNER or SECOND_CHANCE. Enum.ToString().ToUpperInvariant() would give
    /// SECONDCHANCE, which AuctionService does not recognise.
    /// </summary>
    public static string ToWireValue(this AuctionOrderSource source) =>
        source == AuctionOrderSource.SecondChance ? "SECOND_CHANCE" : "WINNER";
}
