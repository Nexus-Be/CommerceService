namespace Commerce.Application.Services.Cart;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface ICartService
{
    Task<CartDto> GetActiveCartByBuyerIdAsync(Guid buyerId, CancellationToken cancellationToken = default);
}
