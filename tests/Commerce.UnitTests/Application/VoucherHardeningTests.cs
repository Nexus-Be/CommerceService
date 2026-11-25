namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Discount;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Xunit;

public class VoucherHardeningTests : IDisposable
{
    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Guid _seller = Guid.NewGuid();
    private readonly Guid _buyer = Guid.NewGuid();

    public void Dispose() => _db.Dispose();

    private async Task<DiscountCode> AddShopVoucherAsync(string code, decimal minOrder = 0m, int? maxTotal = null, int maxPerUser = 1)
    {
        var v = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            ScopeType = DiscountScopeType.Shop,
            OwnerSellerId = _seller,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10_000m,
            MinOrderAmount = minOrder,
            MaxUsesTotal = maxTotal,
            MaxUsesPerUser = maxPerUser,
            Status = DiscountStatus.Active,
            IsVisible = true,
            CreatedBy = _seller
        };
        _db.DiscountCodes.Add(v);
        await _db.SaveChangesAsync();
        return v;
    }

    private VoucherDiscountCalculator Calculator() => new(_db, new VoucherCalculationEngine());

    private VoucherCalculationRequest Request(params string[] codes)
    {
        var item = new CalculationItemInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _seller, "P", 1, 200_000m);
        return new VoucherCalculationRequest(
            _buyer, codes,
            new Dictionary<Guid, decimal> { [_seller] = 200_000m },
            new Dictionary<Guid, decimal> { [_seller] = 30_000m },
            new[] { item });
    }

    [Fact]
    public async Task UnknownCode_IsRejected_WithNotFoundReason()
    {
        var result = await Calculator().CalculateDiscountsAsync(Request("NOPE"));
        result.AppliedVouchers.Should().BeEmpty();
        result.RejectedVouchers.Should().Contain("NOPE");
        result.RejectedReasons!["NOPE"].Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task BelowMinimumOrder_IsRejected_WithMinOrderReason_AndNotApplied()
    {
        await AddShopVoucherAsync("BIGMIN", minOrder: 5_000_000m);
        var result = await Calculator().CalculateDiscountsAsync(Request("BIGMIN"));
        result.AppliedVouchers.Should().BeEmpty();
        result.RejectedReasons!["BIGMIN"].Should().Be("MIN_ORDER_NOT_MET");
    }

    [Fact]
    public async Task BuyerWhoReachedPerUserLimit_IsToldAtQuoteTime()
    {
        var v = await AddShopVoucherAsync("ONCE", maxPerUser: 1);
        _db.DiscountCodeUsages.Add(new DiscountCodeUsage
        {
            Id = Guid.NewGuid(), DiscountCodeId = v.Id, UserId = _buyer,
            Status = DiscountUsageStatus.Used, DiscountAmount = 10_000m, ReservedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await Calculator().CalculateDiscountsAsync(Request("ONCE"));
        result.AppliedVouchers.Should().BeEmpty();
        result.RejectedReasons!["ONCE"].Should().Be("USER_LIMIT_REACHED");
    }

    [Fact]
    public async Task ReleasedUsage_DoesNotCountAgainstPerUserLimit()
    {
        var v = await AddShopVoucherAsync("AGAIN", maxPerUser: 1);
        _db.DiscountCodeUsages.Add(new DiscountCodeUsage
        {
            Id = Guid.NewGuid(), DiscountCodeId = v.Id, UserId = _buyer,
            Status = DiscountUsageStatus.Released, DiscountAmount = 10_000m, ReservedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await Calculator().CalculateDiscountsAsync(Request("AGAIN"));
        result.AppliedVouchers.Should().Contain("AGAIN");
    }

    [Fact]
    public async Task ValidVoucher_IsApplied_WithAllocatedAmount()
    {
        await AddShopVoucherAsync("OK10");
        var result = await Calculator().CalculateDiscountsAsync(Request("OK10"));
        result.AppliedVouchers.Should().Contain("OK10");
        result.AllocatedUsages!.Sum(u => u.DiscountAmount).Should().Be(10_000m);
    }

    [Fact]
    public async Task Reserve_StopsAtTotalLimit_AndNeverOvershoots()
    {
        var v = await AddShopVoucherAsync("LIMIT1", maxTotal: 1, maxPerUser: 5);
        var lifecycle = new VoucherLifecycleService(_db);
        var usage = new[] { new AllocatedVoucherUsage(v.Id, v.Code, DiscountScopeType.Shop, _seller, 10_000m) };

        await lifecycle.ReserveVouchersAsync(Guid.NewGuid(), usage);
        var second = async () => await lifecycle.ReserveVouchersAsync(Guid.NewGuid(), usage);

        (await second.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("VOUCHER_USAGE_LIMIT_EXCEEDED");
        (await _db.DiscountCodes.FindAsync(v.Id))!.ReservedCount.Should().Be(1);
    }
}
