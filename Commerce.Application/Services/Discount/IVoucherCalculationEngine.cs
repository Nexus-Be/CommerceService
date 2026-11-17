namespace Commerce.Application.Services.Discount;

using Commerce.Domain.Entities;

public interface IVoucherCalculationEngine
{
    EvaluateVouchersResult EvaluateAndApplyVouchers(EvaluateVouchersRequest request);
    decimal CalculateRawDiscount(decimal baseAmount, DiscountCode voucher);
}
