namespace Commerce.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Services.Cart;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/carts")]
public class CartsController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartsController(ICartService cartService)
    {
        _cartService = cartService;
    }

    [HttpGet("active")]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActiveCart([FromQuery] Guid buyerId, CancellationToken ct)
    {
        var cart = await _cartService.GetActiveCartByBuyerIdAsync(buyerId, ct);
        if (cart == null)
        {
            return NotFound(new { message = "Active cart not found for the specified buyer." });
        }

        return Ok(cart);
    }
}
