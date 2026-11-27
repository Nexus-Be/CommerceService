namespace Commerce.UnitTests.Controllers;

using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Controllers;
using Commerce.Application.Services.Order;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

public class OrdersControllerShipmentTrackingRouteTests
{
    [Fact]
    public void GetShipmentTracking_ShouldHaveCanonicalRouteAndLegacyAlias()
    {
        // Arrange
        var method = typeof(OrdersController).GetMethod(nameof(OrdersController.GetShipmentTracking));
        method.Should().NotBeNull();

        // Act
        var httpGetAttrs = method!.GetCustomAttributes<HttpGetAttribute>().ToList();

        // Assert
        httpGetAttrs.Should().HaveCount(2);

        var canonicalRoute = httpGetAttrs.FirstOrDefault(a => a.Template == "{orderId:guid}/shipments");
        canonicalRoute.Should().NotBeNull("Canonical route {orderId:guid}/shipments must be defined");

        var legacyRoute = httpGetAttrs.FirstOrDefault(a => a.Template == "/api/v1/shipments/order/{orderId:guid}");
        legacyRoute.Should().NotBeNull("Legacy alias route /api/v1/shipments/order/{orderId:guid} must be preserved");
    }

    [Fact]
    public async Task GetShipmentTracking_ShouldReturnOk_WhenAuthorized()
    {
        // Arrange
        var mockService = new Mock<IOrderService>();
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var expectedResponse = new ShipmentTrackingResponse(
            OrderId: orderId,
            OrderNo: "ORD-20261126-0001",
            Shipments: new()
        );

        mockService
            .Setup(s => s.GetShipmentTrackingAsync(buyerId, orderId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = new OrdersController(mockService.Object);
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, buyerId.ToString())
        }, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        // Act
        var result = await controller.GetShipmentTracking(orderId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.Value.Should().BeEquivalentTo(expectedResponse);
    }
}
