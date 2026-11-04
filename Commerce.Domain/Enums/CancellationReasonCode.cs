namespace Commerce.Domain.Enums;

public enum CancellationReasonCode
{
    BuyerChangedMind = 0,
    OutOfStock = 1,
    PaymentTimeout = 2,
    SellerRejected = 3,
    Other = 4,
    DeliveryFailed = 5,
    PackageReturned = 6
}
