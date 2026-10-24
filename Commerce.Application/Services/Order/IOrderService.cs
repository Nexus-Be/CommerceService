namespace Commerce.Application.Services.Order;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IOrderService
{
    Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderDto>> ListByBuyerIdAsync(Guid buyerId, CancellationToken cancellationToken = default);
}
