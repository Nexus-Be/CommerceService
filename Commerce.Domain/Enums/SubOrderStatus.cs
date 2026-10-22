namespace Commerce.Domain.Enums;

public enum SubOrderStatus
{
    PendingPayment = 0,
    Paid = 1,
    SellerAccepted = 2,
    Shipped = 3,
    Delivered = 4,
    Completed = 5,
    Cancelled = 6
}
