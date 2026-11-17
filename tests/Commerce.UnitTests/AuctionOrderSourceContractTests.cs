namespace Commerce.UnitTests;

using Commerce.Domain.Enums;
using Xunit;

public sealed class AuctionOrderSourceContractTests
{
    [Theory]
    [InlineData(AuctionOrderSource.Winner, "WINNER")]
    [InlineData(AuctionOrderSource.SecondChance, "SECOND_CHANCE")]
    public void ToWireValue_MatchesTheContractWithAuctionService(AuctionOrderSource source, string expected)
    {
        // AuctionService compares with SECOND_CHANCE; Enum.ToString().ToUpperInvariant() would send SECONDCHANCE.
        Assert.Equal(expected, source.ToWireValue());
    }
}
