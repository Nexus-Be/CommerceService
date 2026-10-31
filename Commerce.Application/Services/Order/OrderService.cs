namespace Commerce.Application.Services.Order;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Common;
using Commerce.Application.Common.Models;
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;

public sealed class OrderService : IOrderService
{
    private readonly ICommerceDbContext _dbContext;

    public OrderService(ICommerceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OrderDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("ORDER_NOT_FOUND", "Order", id);

        return new OrderDto(
            order.Id,
            order.BuyerId,
            order.Status.ToString(),
            order.TotalAmount,
            order.PaymentDeadlineAt);
    }

    public async Task<PagedData<OrderDto>> ListByBuyerIdAsync(Guid buyerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (safePage, safePageSize) = PaginationConstants.Normalize(page, pageSize);

        var query = _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == buyerId);

        var totalCount = await query.CountAsync(cancellationToken);

        var orders = await query
            .OrderByDescending(o => o.PaymentDeadlineAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(o => new OrderDto(
                o.Id,
                o.BuyerId,
                o.Status.ToString(),
                o.TotalAmount,
                o.PaymentDeadlineAt))
            .ToListAsync(cancellationToken);

        return new PagedData<OrderDto>(
            orders,
            PaginationMetadata.Create(safePage, safePageSize, totalCount));
    }
}
