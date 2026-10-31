namespace Commerce.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Services.Cart;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/carts")]
[RequireRole("buyer", "admin", "staff")]
public class CartsController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartsController(ICartService cartService)
    {
        _cartService = cartService;
    }

    /// <summary>Gets active shopping cart by buyer ID.</summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetActiveCart([FromQuery] Guid? buyerId, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var currentUserId, out var authError))
        {
            return authError;
        }

        var targetBuyerId = buyerId ?? currentUserId;
        if (!this.EnsureOwnership(targetBuyerId, out var ownershipError))
        {
            return ownershipError;
        }

        var cart = await _cartService.GetActiveCartByBuyerIdAsync(targetBuyerId, ct);
        return Ok(cart);
    }

    /// <summary>CART-01: Gets or creates the active shopping cart for the authenticated buyer.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyCart(CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var cart = await _cartService.GetOrCreateActiveCartAsync(buyerId, ct);
        return Ok(cart);
    }

    /// <summary>CART-02: Adds a product SKU to the buyer's active cart.</summary>
    [HttpPost("me/items")]
    public async Task<IActionResult> AddItem([FromBody] AddCartItemRequest request, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var cart = await _cartService.AddItemAsync(buyerId, request, ct);
        return Ok(cart);
    }

    /// <summary>CART-03: Updates quantity of an existing item in the active cart.</summary>
    [HttpPatch("me/items/{cartItemId:guid}")]
    public async Task<IActionResult> UpdateItemQuantity([FromRoute] Guid cartItemId, [FromBody] UpdateCartItemQuantityRequest request, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var cart = await _cartService.UpdateItemQuantityAsync(buyerId, cartItemId, request, ct);
        return Ok(cart);
    }

    /// <summary>CART-04: Removes a single item from the active cart.</summary>
    [HttpDelete("me/items/{cartItemId:guid}")]
    public async Task<IActionResult> RemoveItem([FromRoute] Guid cartItemId, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var cart = await _cartService.RemoveItemAsync(buyerId, cartItemId, ct);
        return Ok(cart);
    }

    /// <summary>CART-05: Bulk removes selected items from the active cart.</summary>
    [HttpPost("me/items:bulk-remove")]
    public async Task<IActionResult> BulkRemove([FromBody] BulkRemoveCartItemsRequest request, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var cart = await _cartService.BulkRemoveOrClearAsync(buyerId, request, ct);
        return Ok(cart);
    }

    /// <summary>CART-05 (Shortcut): Clears all items from the active cart.</summary>
    [HttpDelete("me/clear")]
    public async Task<IActionResult> ClearCart(CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var cart = await _cartService.BulkRemoveOrClearAsync(buyerId, new BulkRemoveCartItemsRequest(null, ClearAll: true), ct);
        return Ok(cart);
    }

    /// <summary>CART-08: Gets summary badge count and subtotal for header display.</summary>
    [HttpGet("me/summary")]
    public async Task<IActionResult> GetCartSummary(CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var summary = await _cartService.GetCartSummaryAsync(buyerId, ct);
        return Ok(summary);
    }

    /// <summary>CART-09: Calculates totals for selected items in the cart (Shopee checkbox mechanism).</summary>
    [HttpPost("me/calculate")]
    public async Task<IActionResult> CalculateSelected([FromBody] CalculateSelectedCartItemsRequest request, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var result = await _cartService.CalculateSelectedItemsAsync(buyerId, request, ct);
        return Ok(result);
    }

    /// <summary>CART-06: Pre-checkout validation for stock availability and price freshness.</summary>
    [HttpPost("me/validate")]
    public async Task<IActionResult> ValidateCart([FromBody] ValidateCartRequest request, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var result = await _cartService.ValidateCartAsync(buyerId, request, ct);
        return Ok(result);
    }

    /// <summary>CART-10: Merges guest cart items into buyer's active cart upon login.</summary>
    [HttpPost("me/merge")]
    public async Task<IActionResult> MergeGuestCart([FromBody] MergeGuestCartRequest request, CancellationToken ct)
    {
        if (!this.TryGetUserId(out var buyerId, out var authError))
        {
            return authError;
        }

        var cart = await _cartService.MergeGuestCartAsync(buyerId, request, ct);
        return Ok(cart);
    }
}
