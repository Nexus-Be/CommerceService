namespace Commerce.Application.Services.Cart;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface ICartService
{
    Task<CartDto> GetActiveCartByBuyerIdAsync(Guid buyerId, CancellationToken cancellationToken = default);

    Task<CartDto> GetOrCreateActiveCartAsync(Guid buyerId, CancellationToken cancellationToken = default);

    Task<CartDto> AddItemAsync(Guid buyerId, AddCartItemRequest request, CancellationToken cancellationToken = default);

    Task<CartDto> UpdateItemQuantityAsync(Guid buyerId, Guid cartItemId, UpdateCartItemQuantityRequest request, CancellationToken cancellationToken = default);

    Task<CartDto> RemoveItemAsync(Guid buyerId, Guid cartItemId, CancellationToken cancellationToken = default);

    Task<CartDto> BulkRemoveOrClearAsync(Guid buyerId, BulkRemoveCartItemsRequest request, CancellationToken cancellationToken = default);

    Task<CartSummaryResponse> GetCartSummaryAsync(Guid buyerId, CancellationToken cancellationToken = default);

    Task<CalculateSelectedCartResponse> CalculateSelectedItemsAsync(Guid buyerId, CalculateSelectedCartItemsRequest request, CancellationToken cancellationToken = default);

    Task<ValidateCartResponse> ValidateCartAsync(Guid buyerId, ValidateCartRequest request, CancellationToken cancellationToken = default);

    Task<CartDto> MergeGuestCartAsync(Guid buyerId, MergeGuestCartRequest request, CancellationToken cancellationToken = default);
}
