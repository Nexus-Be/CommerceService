namespace Commerce.UnitTests;

using Commerce.Application.Interfaces;
using Xunit;

public sealed class UserPenaltyContractTests
{
    // UserService rejects any other value with 400 (its PenaltyViolationType enum), which drops the penalty.
    private static readonly string[] AcceptedByUserService =
        { "PaymentTimeout", "AuctionNonPayment", "CancellationWithoutReason", "Fraud", "Other" };

    [Theory]
    [InlineData(UserPenaltyViolationTypes.SellerRejectedPaidOrder)]
    [InlineData(UserPenaltyViolationTypes.SellerResponseTimeout)]
    public void SellerPenaltyTypes_AreValuesUserServiceAccepts(string violationType)
    {
        Assert.Contains(violationType, AcceptedByUserService);
    }
}
