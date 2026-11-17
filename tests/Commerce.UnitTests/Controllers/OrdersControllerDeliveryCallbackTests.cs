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
    public async Task UpdateDeliveryStatus_ShouldReturn401Unauthorized_WhenHeaderIsMissing()
    {
        // Arrange
        var controller = CreateController(headerKey: null);
        var subOrderId = Guid.NewGuid();
        var request = new UpdateDeliveryStatusRequest("Delivered");

        // Act
        var result = await controller.UpdateDeliveryStatus(subOrderId, request, _configuration, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        _mockOrderService.Verify(
            s => s.UpdateSubOrderDeliveryStatusAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateDeliveryStatus_ShouldReturn401Unauthorized_WhenHeaderIsInvalid()
    {
        // Arrange
        var controller = CreateController(headerKey: "wrong-secret-key");
        var subOrderId = Guid.NewGuid();
        var request = new UpdateDeliveryStatusRequest("Delivered");

        // Act
        var result = await controller.UpdateDeliveryStatus(subOrderId, request, _configuration, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

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
            .Setup(s => s.UpdateSubOrderDeliveryStatusAsync(subOrderId, request, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await controller.UpdateDeliveryStatus(subOrderId, request, _configuration, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);

        _mockOrderService.Verify(
            s => s.UpdateSubOrderDeliveryStatusAsync(subOrderId, request, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateDeliveryStatus_ShouldForwardPodAndCourierInfo_WhenProvidedInRequest()
    {
        // Arrange
        var controller = CreateController(headerKey: "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e");
        var subOrderId = Guid.NewGuid();
        var deliveryTime = DateTimeOffset.UtcNow;
        var request = new UpdateDeliveryStatusRequest(
            Status: "Delivered",
            DeliveredAt: deliveryTime,
            CarrierCode: "GHN",
            TrackingCode: "GHN-SGN-1029384",
            Note: "Handed over to recipient",
            Location: "District 1, HCMC",
            PodUrl: "https://cdn.ghn.vn/pod/20261117/sign_001.jpg",
            ShipperName: "Nguyen Van C",
            ShipperPhone: "0987654321");

        _mockOrderService
            .Setup(s => s.UpdateSubOrderDeliveryStatusAsync(subOrderId, request, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await controller.UpdateDeliveryStatus(subOrderId, request, _configuration, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);

        _mockOrderService.Verify(
            s => s.UpdateSubOrderDeliveryStatusAsync(subOrderId, It.Is<UpdateDeliveryStatusRequest>(r =>
                r.PodUrl == "https://cdn.ghn.vn/pod/20261117/sign_001.jpg" &&
                r.ShipperName == "Nguyen Van C" &&
                r.ShipperPhone == "0987654321"), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
