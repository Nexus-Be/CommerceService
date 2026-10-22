namespace Commerce.Domain.Enums;

public enum OrderStatus
{
    PendingPayment = 0,
    Paid = 1,
    Failed = 2,
    Expired = 3,
    Cancelled = 4
}
