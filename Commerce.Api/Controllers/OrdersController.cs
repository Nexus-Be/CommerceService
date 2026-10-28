namespace Commerce.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Services.Order;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var order = await _orderService.GetByIdAsync(id, ct);
        if (order == null)
        {
            return NotFound(new { message = "Order not found with the specified ID." });
        }

        return Ok(order);
    }

    [HttpGet("buyer/{buyerId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByBuyer([FromRoute] Guid buyerId, CancellationToken ct)
    {
        var orders = await _orderService.ListByBuyerIdAsync(buyerId, ct);
        return Ok(orders);
    }
}
