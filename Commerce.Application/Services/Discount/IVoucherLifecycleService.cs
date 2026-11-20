namespace Commerce.Application.Services.Discount;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IVoucherLifecycleService
{
    Task<IReadOnlyList<Guid>> ReserveVouchersAsync(
        Guid buyerId,
        IReadOnlyList<AllocatedVoucherUsage> vouchers,
        CancellationToken ct = default);

    Task CommitOrderVouchersAsync(
        Guid orderId,
        CancellationToken ct = default);

    Task ReleaseOrderVouchersAsync(
        Guid orderId,
        CancellationToken ct = default);

    Task ReleaseSubOrderVouchersAsync(
        Guid subOrderId,
        CancellationToken ct = default);

    Task ReleaseReservationsAsync(
        IReadOnlyList<Guid> usageIds,
        CancellationToken ct = default);

    Task ReleaseExpiredReservationsAsync(
        CancellationToken ct = default);
}
