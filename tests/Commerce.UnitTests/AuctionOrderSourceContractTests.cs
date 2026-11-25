namespace Commerce.UnitTests;

using Commerce.Domain.Enums;
using Xunit;

public sealed class AuctionOrderSourceContractTests
{
    [Theory]
    [InlineData(AuctionOrderSource.Winner, "WINNER")]
    [InlineData(AuctionOrderSource.SecondChance, "SECOND_CHANCE")]
    [InlineData(AuctionOrderSource.BuyNow, "BUY_NOW")]
    public void ToWireValue_MatchesTheContractWithAuctionService(AuctionOrderSource source, string expected)
    {
        // AuctionService compares with SECOND_CHANCE or BUY_NOW; Enum.ToString().ToUpperInvariant() would send SECONDCHANCE or BUYNOW.
        Assert.Equal(expected, source.ToWireValue());
    }

    [Fact]
    public void ToWireValue_NeverProducesConcatenatedStringsWithoutUnderscore()
    {
        foreach (var source in Enum.GetValues<AuctionOrderSource>())
        {
            var wire = source.ToWireValue();
            if (source == AuctionOrderSource.SecondChance)
            {
                Assert.NotEqual("SECONDCHANCE", wire);
                Assert.Equal("SECOND_CHANCE", wire);
            }
            else if (source == AuctionOrderSource.BuyNow)
            {
                Assert.NotEqual("BUYNOW", wire);
                Assert.Equal("BUY_NOW", wire);
            }
        }
    }
}
