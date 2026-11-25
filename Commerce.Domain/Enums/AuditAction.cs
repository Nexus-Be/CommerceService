namespace Commerce.Domain.Enums;

public enum AuditAction
{
    CartCheckout = 0,
    OrderCreate = 1,
    OrderPay = 2,
    OrderAccept = 3,
    OrderCancel = 4,
    DiscountCreate = 5,
    DiscountUpdate = 6,
    InvoiceGenerate = 7,
    DiscountStatusChange = 8,
    DiscountDelete = 9,
    PlatformFeeUpdate = 10
}
