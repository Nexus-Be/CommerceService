namespace Commerce.Domain.Enums;

public enum AuctionOrderSource
{
    Winner = 1,
    SecondChance = 2,
    /// <summary>Bought at the Buy Now price: the goods are already covered by holds, only shipping is left to pay.</summary>
    BuyNow = 3
}
