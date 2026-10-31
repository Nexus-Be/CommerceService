namespace Commerce.Application.Services.Cart;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;

public sealed class CartService : ICartService
{
    private readonly ICommerceDbContext _dbContext;

    public CartService(ICommerceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CartDto> GetActiveCartByBuyerIdAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var cart = await _dbContext.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.Status == Domain.Enums.CartStatus.Active, cancellationToken)
            ?? throw new EntityNotFoundException("CART_NOT_FOUND", "Cart", buyerId);

        var items = cart.Items.Select(i => new CartItemDto(
            i.Id,
            i.ProductId,
            i.SkuId,
            i.SellerId,
            i.ProductNameSnapshot,
            i.Quantity,
            i.UnitPriceSnapshot)).ToList();

        return new CartDto(
            cart.Id,
            cart.BuyerId,
            cart.Status.ToString(),
            cart.ItemCount,
            cart.SubtotalAmount,
            items);
    }
}
