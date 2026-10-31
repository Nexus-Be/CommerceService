namespace Commerce.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Extensions;
using Commerce.Api.Filters;
using Commerce.Application.Common.Models;
using Commerce.Application.Services.Order;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/orders")]
[RequireRole]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>Gets order details by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var order = await _orderService.GetByIdAsync(id, ct);

        if (!this.EnsureOwnership(order.BuyerId, out var ownershipError))
        {
            return ownershipError;
        }

        return Ok(order);
    }

    /// <summary>Lists orders for a specific buyer.</summary>
    [HttpGet("buyer/{buyerId:guid}")]
    public async Task<IActionResult> ListByBuyer(
        [FromRoute] Guid buyerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!this.EnsureOwnership(buyerId, out var ownershipError))
        {
            return ownershipError;
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        PagedData<OrderDto> orders = await _orderService.ListByBuyerIdAsync(buyerId, page, pageSize, ct);
        return Ok(orders);
    }
}
