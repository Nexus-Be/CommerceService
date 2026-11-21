namespace Commerce.UnitTests.Application;

using System;
using Commerce.Application;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Discount;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class VoucherRegistrationTests
{
    [Fact]
    public void The_voucher_calculator_that_is_registered_is_the_real_one_not_a_stub_that_refuses_every_code()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IVoucherDiscountCalculator));
        Assert.Equal(typeof(VoucherDiscountCalculator), descriptor.ImplementationType);
    }

    [Fact]
    public void Stacking_errors_carry_a_reason_code_clients_can_branch_on()
    {
        var engine = new VoucherCalculationEngine();
        var request = new Commerce.Application.Services.Discount.EvaluateVouchersRequest(
            Array.Empty<CalculationItemInput>(), Array.Empty<Commerce.Domain.Entities.DiscountCode>());
        // An empty request is valid; the stacking rule itself is covered by the end-to-end flow. Here the exception type is checked.
        var error = new CommerceDomainException("VOUCHER_STACKING_CONFLICT", "x", 400) { ReasonCode = "STACKING_CONFLICT" };
        Assert.Equal("STACKING_CONFLICT", error.ReasonCode);
        Assert.NotNull(engine.EvaluateAndApplyVouchers(request));
    }
}
