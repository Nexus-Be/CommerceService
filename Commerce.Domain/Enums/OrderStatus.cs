namespace Commerce.Domain.Enums;

public enum OrderStatus
{
    PendingPayment = 0,
    PaymentProcessing = 1,
    Paid = 2,
    Cancelled = 3,
    Expired = 4
}
