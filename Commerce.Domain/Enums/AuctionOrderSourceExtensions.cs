namespace Commerce.Domain.Enums;

public static class AuctionOrderSourceExtensions
{
    /// <summary>
    /// Value used in the contract with AuctionService: WINNER, SECOND_CHANCE, or BUY_NOW.
    /// Enum.ToString().ToUpperInvariant() would give SECONDCHANCE or BUYNOW, which AuctionService does not recognise.
    /// </summary>
    public static string ToWireValue(this AuctionOrderSource source) =>
        source switch
        {
            AuctionOrderSource.SecondChance => "SECOND_CHANCE",
            AuctionOrderSource.BuyNow => "BUY_NOW",
            _ => "WINNER"
        };
}
