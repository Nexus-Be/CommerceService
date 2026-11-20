namespace Commerce.Application.Services.Discount;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;

public sealed class VoucherLifecycleService : IVoucherLifecycleService
{
    private readonly ICommerceDbContext _db;

    public VoucherLifecycleService(ICommerceDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Guid>> ReserveVouchersAsync(
        Guid buyerId,
        IReadOnlyList<AllocatedVoucherUsage> vouchers,
        CancellationToken ct = default)
    {
        if (vouchers == null || vouchers.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var createdUsageIds = new List<Guid>();
        var isPostgres = _db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

        // Group allocated vouchers by DiscountCodeId to avoid duplicate locks
        var distinctVoucherIds = vouchers.Select(v => v.DiscountCodeId).Distinct().ToList();

        // If PostgreSQL is used and not in an ambient transaction, manage advisory locks
        foreach (var voucherId in distinctVoucherIds)
        {
            if (isPostgres)
            {
                // Acquire transaction-scoped advisory lock for (voucherId, buyerId)
                await _db.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtext(concat({0}, ':', {1})));",
                    new object[] { voucherId.ToString(), buyerId.ToString() },
                    ct
                );
            }

            var voucher = await _db.DiscountCodes.FirstOrDefaultAsync(d => d.Id == voucherId && !d.IsDeleted, ct);
            if (voucher == null)
            {
                throw new CommerceDomainException("VOUCHER_NOT_FOUND", "Voucher was not found.", 404);
            }

            var now = DateTimeOffset.UtcNow;
            if (voucher.Status != DiscountStatus.Active || (voucher.StartsAt.HasValue && now < voucher.StartsAt.Value) || (voucher.EndsAt.HasValue && now >= voucher.EndsAt.Value))
            {
                throw new CommerceDomainException("VOUCHER_EXPIRED", $"Voucher '{voucher.Code}' is inactive or expired.", 400);
            }

            if (voucher.MaxUsesTotal.HasValue && (voucher.UsedCount + voucher.ReservedCount) >= voucher.MaxUsesTotal.Value)
            {
                throw new CommerceDomainException("VOUCHER_USAGE_LIMIT_EXCEEDED", $"Voucher '{voucher.Code}' has reached its total usage limit.", 400);
            }

            if (voucher.MaxUsesPerUser.HasValue)
            {
                var userUsageCount = await _db.DiscountCodeUsages.CountAsync(
                    u => u.DiscountCodeId == voucher.Id && u.UserId == buyerId && u.Status != DiscountUsageStatus.Released,
                    ct
                );
                if (userUsageCount >= voucher.MaxUsesPerUser.Value)
                {
                    throw new CommerceDomainException("VOUCHER_USER_LIMIT_EXCEEDED", $"You have reached the maximum redemption limit for voucher '{voucher.Code}'.", 400);
                }
            }

            // Find all usages allocated to this voucher
            var matchingAllocations = vouchers.Where(v => v.DiscountCodeId == voucherId).ToList();
            var totalAllocatedDiscount = matchingAllocations.Sum(a => a.DiscountAmount);

            voucher.ReservedCount += 1;

            var usage = new DiscountCodeUsage
            {
                Id = Guid.NewGuid(),
                DiscountCodeId = voucher.Id,
                UserId = buyerId,
                OrderId = null,
                SubOrderId = null,
                Status = DiscountUsageStatus.Reserved,
                DiscountAmount = totalAllocatedDiscount,
                ReservedAt = now
            };

            _db.DiscountCodeUsages.Add(usage);
            createdUsageIds.Add(usage.Id);
        }

        await _db.SaveChangesAsync(ct);
        return createdUsageIds;
    }

    public async Task CommitOrderVouchersAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        var usages = await _db.DiscountCodeUsages
            .Where(u => u.OrderId == orderId && u.Status == DiscountUsageStatus.Reserved)
            .ToListAsync(ct);

        if (usages.Count == 0)
        {
            return;
        }

        var voucherIds = usages.Select(u => u.DiscountCodeId).Distinct().ToList();
        var vouchers = await _db.DiscountCodes
            .Where(v => voucherIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var usage in usages)
        {
            usage.Status = DiscountUsageStatus.Used;
            usage.UsedAt = now;

            if (vouchers.TryGetValue(usage.DiscountCodeId, out var voucher))
            {
                voucher.ReservedCount = Math.Max(0, voucher.ReservedCount - 1);
                voucher.UsedCount += 1;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ReleaseOrderVouchersAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        var usages = await _db.DiscountCodeUsages
            .Where(u => u.OrderId == orderId && u.Status == DiscountUsageStatus.Used)
            .ToListAsync(ct);

        if (usages.Count == 0)
        {
            return;
        }

        var voucherIds = usages.Select(u => u.DiscountCodeId).Distinct().ToList();
        var vouchers = await _db.DiscountCodes
            .Where(v => voucherIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var usage in usages)
        {
            usage.Status = DiscountUsageStatus.Released;
            usage.ReleasedAt = now;

            if (vouchers.TryGetValue(usage.DiscountCodeId, out var voucher))
            {
                voucher.UsedCount = Math.Max(0, voucher.UsedCount - 1);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ReleaseSubOrderVouchersAsync(
        Guid subOrderId,
        CancellationToken ct = default)
    {
        var usages = await _db.DiscountCodeUsages
            .Where(u => u.SubOrderId == subOrderId && u.Status == DiscountUsageStatus.Used)
            .ToListAsync(ct);

        if (usages.Count == 0)
        {
            return;
        }

        var voucherIds = usages.Select(u => u.DiscountCodeId).Distinct().ToList();
        var vouchers = await _db.DiscountCodes
            .Where(v => voucherIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var usage in usages)
        {
            usage.Status = DiscountUsageStatus.Released;
            usage.ReleasedAt = now;

            if (vouchers.TryGetValue(usage.DiscountCodeId, out var voucher))
            {
                voucher.UsedCount = Math.Max(0, voucher.UsedCount - 1);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ReleaseReservationsAsync(
        IReadOnlyList<Guid> usageIds,
        CancellationToken ct = default)
    {
        if (usageIds == null || usageIds.Count == 0)
        {
            return;
        }

        var usages = await _db.DiscountCodeUsages
            .Where(u => usageIds.Contains(u.Id) && u.Status == DiscountUsageStatus.Reserved)
            .ToListAsync(ct);

        if (usages.Count == 0)
        {
            return;
        }

        var voucherIds = usages.Select(u => u.DiscountCodeId).Distinct().ToList();
        var vouchers = await _db.DiscountCodes
            .Where(v => voucherIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var usage in usages)
        {
            usage.Status = DiscountUsageStatus.Released;
            usage.ReleasedAt = now;

            if (vouchers.TryGetValue(usage.DiscountCodeId, out var voucher))
            {
                voucher.ReservedCount = Math.Max(0, voucher.ReservedCount - 1);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ReleaseExpiredReservationsAsync(
        CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-16);
        var expiredReservations = await _db.DiscountCodeUsages
            .Where(u => u.Status == DiscountUsageStatus.Reserved && u.ReservedAt < cutoff)
            .ToListAsync(ct);

        if (expiredReservations.Count == 0)
        {
            return;
        }

        var voucherIds = expiredReservations.Select(u => u.DiscountCodeId).Distinct().ToList();
        var vouchers = await _db.DiscountCodes
            .Where(v => voucherIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var usage in expiredReservations)
        {
            usage.Status = DiscountUsageStatus.Released;
            usage.ReleasedAt = now;

            if (vouchers.TryGetValue(usage.DiscountCodeId, out var voucher))
            {
                voucher.ReservedCount = Math.Max(0, voucher.ReservedCount - 1);
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
