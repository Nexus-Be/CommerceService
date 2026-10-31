namespace Commerce.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Services.Cart;
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
}
