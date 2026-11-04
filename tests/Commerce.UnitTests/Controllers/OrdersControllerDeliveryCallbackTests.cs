namespace Commerce.UnitTests.Controllers;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Controllers;
using Commerce.Application.Services.Order;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

public class OrdersControllerDeliveryCallbackTests
{
    private readonly Mock<IOrderService> _mockOrderService = new();
    private readonly IConfiguration _configuration;

    public OrdersControllerDeliveryCallbackTests()
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InternalSecurity:ServiceKey"] = "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"
        });
        _configuration = configBuilder.Build();
    }

    private OrdersController CreateController(string? headerKey = null)
    {
        var controller = new OrdersController(_mockOrderService.Object);
        var httpContext = new DefaultHttpContext();
        if (headerKey != null)
        {
            httpContext.Request.Headers["X-Internal-Service-Key"] = headerKey;
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    [Fact]
    public async Task UpdateDeliveryStatus_ShouldReturn403Forbidden_WhenHeaderIsMissing()
    {
        // Arrange
        var controller = CreateController(headerKey: null);
        var subOrderId = Guid.NewGuid();
        var request = new UpdateDeliveryStatusRequest("Delivered");

        // Act
        var result = await controller.UpdateDeliveryStatus(subOrderId, request, _configuration, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        _mockOrderService.Verify(
            s => s.UpdateSubOrderDeliveryStatusAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateDeliveryStatus_ShouldReturn403Forbidden_WhenHeaderIsInvalid()
    {
        // Arrange
        var controller = CreateController(headerKey: "wrong-secret-key");
        var subOrderId = Guid.NewGuid();
        var request = new UpdateDeliveryStatusRequest("Delivered");

        // Act
        var result = await controller.UpdateDeliveryStatus(subOrderId, request, _configuration, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        _mockOrderService.Verify(
            s => s.UpdateSubOrderDeliveryStatusAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateDeliveryStatus_ShouldReturn200Ok_AndCallOrderService_WhenHeaderIsValid()
    {
        // Arrange
        var controller = CreateController(headerKey: "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e");
        var subOrderId = Guid.NewGuid();
        var deliveryTime = DateTimeOffset.UtcNow;
        var request = new UpdateDeliveryStatusRequest("Delivered", deliveryTime);

        _mockOrderService
            .Setup(s => s.UpdateSubOrderDeliveryStatusAsync(subOrderId, "Delivered", deliveryTime, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await controller.UpdateDeliveryStatus(subOrderId, request, _configuration, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);

        _mockOrderService.Verify(
            s => s.UpdateSubOrderDeliveryStatusAsync(subOrderId, "Delivered", deliveryTime, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
