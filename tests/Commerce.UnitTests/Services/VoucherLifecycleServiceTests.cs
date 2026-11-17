namespace Commerce.UnitTests.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Commerce.Application.Services.Discount;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;

public class VoucherLifecycleServiceTests
{
    private readonly TestCommerceDbContext _db;
    private readonly VoucherLifecycleService _service;

    public VoucherLifecycleServiceTests()
    {
        _db = TestDbContextFactory.CreateInMemoryDbContext();
        _service = new VoucherLifecycleService(_db);
    }

    [Fact]
    public async Task ReserveVouchersAsync_ShouldSucceed_WhenQuotaAvailable()
    {
        // Arrange
        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "TESTVOUCHER",
            Name = "Test Voucher",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            MaxUsesTotal = 10,
            MaxUsesPerUser = 1,
            UsedCount = 0,
            ReservedCount = 0,
            Status = DiscountStatus.Active
        };
        _db.DiscountCodes.Add(voucher);
        await _db.SaveChangesAsync();

        var buyerId = Guid.NewGuid();
        var usage = new AllocatedVoucherUsage(voucher.Id, voucher.Code, DiscountScopeType.System, null, 10000m);

        // Act
        var usageIds = await _service.ReserveVouchersAsync(buyerId, new List<AllocatedVoucherUsage> { usage });

        // Assert
        usageIds.Should().HaveCount(1);
        voucher.ReservedCount.Should().Be(1);
        var createdUsage = await _db.DiscountCodeUsages.FindAsync(usageIds[0]);
        createdUsage.Should().NotBeNull();
        createdUsage!.Status.Should().Be(DiscountUsageStatus.Reserved);
        createdUsage.DiscountAmount.Should().Be(10000m);
    }

    [Fact]
    public async Task ReserveVouchersAsync_ShouldThrow_WhenGlobalLimitExceeded()
    {
        // Arrange
        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "FLASH1",
            Name = "Flash 1 only",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            MaxUsesTotal = 1,
            UsedCount = 1, // Already reached
            ReservedCount = 0,
            Status = DiscountStatus.Active
        };
        _db.DiscountCodes.Add(voucher);
        await _db.SaveChangesAsync();

        var buyerId = Guid.NewGuid();
        var usage = new AllocatedVoucherUsage(voucher.Id, voucher.Code, DiscountScopeType.System, null, 10000m);

        // Act & Assert
        var act = async () => await _service.ReserveVouchersAsync(buyerId, new List<AllocatedVoucherUsage> { usage });
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_USAGE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task ReserveVouchersAsync_ShouldThrow_WhenUserLimitExceeded()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "ONCE_PER_USER",
            Name = "Once per user",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            MaxUsesTotal = 100,
            MaxUsesPerUser = 1,
            UsedCount = 1,
            ReservedCount = 0,
            Status = DiscountStatus.Active
        };
        _db.DiscountCodes.Add(voucher);

        // Prior usage for this user
        _db.DiscountCodeUsages.Add(new DiscountCodeUsage
        {
            Id = Guid.NewGuid(),
            DiscountCodeId = voucher.Id,
            UserId = buyerId,
            Status = DiscountUsageStatus.Used,
            DiscountAmount = 10000m
        });
        await _db.SaveChangesAsync();

        var usage = new AllocatedVoucherUsage(voucher.Id, voucher.Code, DiscountScopeType.System, null, 10000m);

        // Act & Assert
        var act = async () => await _service.ReserveVouchersAsync(buyerId, new List<AllocatedVoucherUsage> { usage });
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_USER_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task CommitOrderVouchersAsync_ShouldTransitionToUsedAndIncrementCounters()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "COMMITME",
            Name = "Commit Me",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 20000m,
            ReservedCount = 1,
            UsedCount = 0,
            Status = DiscountStatus.Active
        };
        _db.DiscountCodes.Add(voucher);

        var usage = new DiscountCodeUsage
        {
            Id = Guid.NewGuid(),
            DiscountCodeId = voucher.Id,
            UserId = Guid.NewGuid(),
            OrderId = orderId,
            Status = DiscountUsageStatus.Reserved,
            DiscountAmount = 20000m
        };
        _db.DiscountCodeUsages.Add(usage);
        await _db.SaveChangesAsync();

        // Act
        await _service.CommitOrderVouchersAsync(orderId);

        // Assert
        voucher.ReservedCount.Should().Be(0);
        voucher.UsedCount.Should().Be(1);
        usage.Status.Should().Be(DiscountUsageStatus.Used);
        usage.UsedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReleaseOrderVouchersAsync_ShouldTransitionToReleasedAndDecrementUsedCount()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "RELEASEME",
            Name = "Release Me",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 20000m,
            ReservedCount = 0,
            UsedCount = 1,
            Status = DiscountStatus.Active
        };
        _db.DiscountCodes.Add(voucher);

        var usage = new DiscountCodeUsage
        {
            Id = Guid.NewGuid(),
            DiscountCodeId = voucher.Id,
            UserId = Guid.NewGuid(),
            OrderId = orderId,
            Status = DiscountUsageStatus.Used,
            DiscountAmount = 20000m
        };
        _db.DiscountCodeUsages.Add(usage);
        await _db.SaveChangesAsync();

        // Act
        await _service.ReleaseOrderVouchersAsync(orderId);

        // Assert
        voucher.UsedCount.Should().Be(0);
        usage.Status.Should().Be(DiscountUsageStatus.Released);
        usage.ReleasedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReleaseExpiredReservationsAsync_ShouldSweepOrphanReservationsOlderThan16Minutes()
    {
        // Arrange
        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SWEEPME",
            Name = "Sweep Me",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            ReservedCount = 1,
            Status = DiscountStatus.Active
        };
        _db.DiscountCodes.Add(voucher);

        var orphanUsage = new DiscountCodeUsage
        {
            Id = Guid.NewGuid(),
            DiscountCodeId = voucher.Id,
            UserId = Guid.NewGuid(),
            Status = DiscountUsageStatus.Reserved,
            DiscountAmount = 10000m,
            ReservedAt = DateTimeOffset.UtcNow.AddMinutes(-20) // Older than 16 mins
        };
        _db.DiscountCodeUsages.Add(orphanUsage);
        await _db.SaveChangesAsync();

        // Act
        await _service.ReleaseExpiredReservationsAsync();

        // Assert
        voucher.ReservedCount.Should().Be(0);
        orphanUsage.Status.Should().Be(DiscountUsageStatus.Released);
    }
}
