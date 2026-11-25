namespace Commerce.UnitTests.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Commerce.Api.Controllers.Internal;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class LegacySettlementClassesTests
{
    private const string Key = "unit-test-internal-key";

    private static InternalReconciliationController Controller(TestCommerceDbContext db, string? header = Key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["INTERNAL_SERVICE_SECRET"] = Key }).Build();
        var http = new DefaultHttpContext();
        if (header is not null) http.Request.Headers["X-Internal-Service-Key"] = header;
        return new InternalReconciliationController(db, configuration) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static SubOrder Sub(int protocol, bool settled, decimal systemDiscount, SubOrderStatus status = SubOrderStatus.Completed)
    {
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = "SUB-" + Guid.NewGuid().ToString("N")[..8], OrderId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            Status = status, PaymentMethod = PaymentMethod.Wallet, ContactPhone = "0900000000", PaymentDeadlineAt = DateTimeOffset.UtcNow,
            SettlementProtocolVersion = protocol, IsEscrowSettled = settled, SellerReceiveAmount = 180_000m, PlatformFeeAmount = 20_000m, SystemDiscountAmount = systemDiscount
        };
        return sub;
    }

    [Fact]
    public async Task It_groups_old_data_without_changing_anything()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        db.SubOrders.AddRange(
            Sub(1, settled: false, systemDiscount: 0m),     // convertible
            Sub(1, settled: false, systemDiscount: 10_000m), // needs a funding source
            Sub(1, settled: true, systemDiscount: 0m),      // paid by V1
            Sub(2, settled: false, systemDiscount: 0m),     // already V2: not legacy
            Sub(1, settled: false, systemDiscount: 0m, status: SubOrderStatus.Cancelled));
        db.AuctionOrders.Add(new AuctionOrder
        {
            Id = Guid.NewGuid(), OrderNo = "AO-1", AuctionId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(), WinningBidId = Guid.NewGuid(), ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "p", WinningAmount = 1_000_000m, SellerReceiveAmount = 950_000.55m, PlatformFeeAmount = 49_999.45m,
            Status = SubOrderStatus.Completed, PaymentMethod = PaymentMethod.Wallet, PaymentDeadlineAt = DateTimeOffset.UtcNow
        });
        db.SettlementSnapshots.Add(new SettlementSnapshot
        {
            Id = Guid.NewGuid(), BusinessType = "OrderPayout", OrderId = Guid.NewGuid(), SubOrderId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            SellerReceiveAmount = 1m, PlatformFeeAmount = 1m, ShippingAmount = 0m, SubsidyAmount = 0m, OrderEscrowAmount = 2m, DepositEscrowAmount = 0m,
            Status = SettlementSnapshotStatus.NeedsFinance, LastErrorCode = "SUBSIDY_NOT_RESERVED", LastError = "not reserved"
        });
        await db.SaveChangesAsync();

        var result = await Controller(db).GetLegacySettlementClasses();

        var classes = ((OkObjectResult)result).Value.Should().BeOfType<InternalReconciliationController.LegacySettlementClasses>().Subject;
        classes.Counts["V1_UNPAID_CONVERTIBLE"].Should().Be(1);
        classes.Counts["V1_UNPAID_PLATFORM_DISCOUNT"].Should().Be(1);
        classes.Counts["V1_PAID"].Should().Be(1);
        classes.Counts["AUCTION_PAYOUT_PENDING"].Should().Be(1);
        classes.Counts["AUCTION_FRACTIONAL_FEE"].Should().Be(1);
        classes.Counts["PAYOUT_NEEDS_FINANCE"].Should().Be(1);
        classes.Items.Select(i => i.Class).Should().Contain(new[] { "V1_UNPAID_CONVERTIBLE", "V1_UNPAID_PLATFORM_DISCOUNT", "AUCTION_FRACTIONAL_FEE", "PAYOUT_NEEDS_FINANCE" });
        // Read only: nothing was paid, flagged or recalculated.
        (await db.SubOrders.CountAsync(s => s.IsEscrowSettled)).Should().Be(1);
        (await db.AuctionOrders.SingleAsync()).PlatformFeeAmount.Should().Be(49_999.45m);
    }

    [Fact]
    public async Task It_requires_the_internal_service_key()
    {
        using var db = TestDbContextFactory.CreateInMemoryDbContext();

        var result = await Controller(db, header: "wrong").GetLegacySettlementClasses();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }
}
