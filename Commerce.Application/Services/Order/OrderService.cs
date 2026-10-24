namespace Commerce.Application.Services.Order;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Interfaces;

public sealed class OrderService : IOrderService
{
    private readonly ICommerceDbContext _dbContext;

    public OrderService(ICommerceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order == null) return null;

        return new OrderDto(
            order.Id,
            order.BuyerId,
            order.Status.ToString(),
            order.TotalAmount,
            order.PaymentDeadlineAt);
    }

    public async Task<IReadOnlyList<OrderDto>> ListByBuyerIdAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.PaymentDeadlineAt)
            .Select(o => new OrderDto(
                o.Id,
                o.BuyerId,
                o.Status.ToString(),
                o.TotalAmount,
                o.PaymentDeadlineAt))
            .ToListAsync(cancellationToken);

        return orders;
    }
}
