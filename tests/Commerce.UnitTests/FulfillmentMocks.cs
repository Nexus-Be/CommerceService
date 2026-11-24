namespace Commerce.UnitTests;

using System.Threading;
using Commerce.Application.Interfaces;
using Moq;

/// <summary>
/// A stand-in for Fulfillment in tests that are not about the address check: the GHN lists confirm every province, district and ward chain.
/// A test about the check sets up its own answer.
/// </summary>
public static class FulfillmentMocks
{
    public static Mock<IFulfillmentClient> WithConfirmedLocations()
    {
        var mock = new Mock<IFulfillmentClient>();
        mock.Setup(f => f.ValidateLocationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LocationCheckResult.Confirmed());
        return mock;
    }
}
