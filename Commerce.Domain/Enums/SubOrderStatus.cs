namespace Commerce.Domain.Enums;

public enum SubOrderStatus
{
    PendingPayment = 0,
    Paid = 1,
    Confirmed = 2,
    Shipping = 3,
    Delivered = 4,
    Completed = 5,
    Cancelled = 6,
    Refunded = 7
}
