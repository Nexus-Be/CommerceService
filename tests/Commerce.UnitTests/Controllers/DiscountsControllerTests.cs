namespace Commerce.UnitTests.Controllers;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using Commerce.Api.Controllers;
using Commerce.Application.Common.Models;
using Commerce.Application.Services.Discount;
using Commerce.Domain.Enums;

public class DiscountsControllerTests
{
    private readonly Mock<IDiscountService> _mockDiscountService = new();

    private DiscountsController CreateController(Guid? userId = null, string? role = null)
    {
        var controller = new DiscountsController(_mockDiscountService.Object);
        var httpContext = new DefaultHttpContext();

        if (userId.HasValue)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.Value.ToString()),
                new("sub", userId.Value.ToString())
            };

            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
                claims.Add(new Claim("role", role));
            }

            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            httpContext.Request.Headers["x-user-id"] = userId.Value.ToString();
            if (!string.IsNullOrEmpty(role))
            {
                httpContext.Request.Headers["x-user-roles"] = role;
            }
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    [Fact]
    public async Task GetAvailableVouchers_ReturnsOk_WithPagedData()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var controller = CreateController(buyerId, "buyer");
        var pagedData = new PagedData<BuyerAvailableVoucherDto>(
            new List<BuyerAvailableVoucherDto>(),
            PaginationMetadata.Create(1, 20, 0)
        );

        _mockDiscountService
            .Setup(s => s.GetAvailableVouchersAsync(buyerId, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedData);

        // Act
        var result = await controller.GetAvailableVouchers(null, null, 1, 20);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(pagedData);
    }

    [Fact]
    public async Task ValidateVoucher_ReturnsOk_WhenValid()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var controller = CreateController(buyerId, "buyer");
        var request = new ValidateVoucherRequest("SUMMER50", new List<Guid> { Guid.NewGuid() });
        var expectedResponse = new ValidateVoucherResponse(true, Guid.NewGuid(), "SUMMER50", DiscountScopeType.Shop, 50000m);

        _mockDiscountService
            .Setup(s => s.ValidateVoucherAsync(buyerId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await controller.ValidateVoucher(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task RecommendVouchers_ReturnsOk_WithRecommendations()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var controller = CreateController(buyerId, "buyer");
        var request = new RecommendVouchersRequest(new List<Guid> { Guid.NewGuid() });
        var expectedResponse = new RecommendVouchersResponse(
            new List<string> { "SYS10K" },
            new TierSavingsDto(0m, 0m, 10000m, 10000m)
        );

        _mockDiscountService
            .Setup(s => s.RecommendVouchersAsync(buyerId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await controller.RecommendVouchers(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task CreateDiscount_ReturnsCreatedAtAction_WhenValid()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var controller = CreateController(sellerId, "seller");
        var request = new CreateDiscountRequest(
            Code: "SHOP10K",
            Name: "Shop 10K",
            ScopeType: DiscountScopeType.Shop,
            OwnerSellerId: sellerId,
            DiscountType: DiscountType.FixedAmount,
            DiscountValue: 10000m
        );

        var discountDetail = new DiscountDetailResponse(
            Id: Guid.NewGuid(),
            Code: "SHOP10K",
            Name: "Shop 10K",
            ScopeType: DiscountScopeType.Shop,
            OwnerSellerId: sellerId,
            DiscountType: DiscountType.FixedAmount,
            DiscountValue: 10000m,
            MinOrderAmount: 0m,
            MaxDiscountAmount: null,
            MaxUsesTotal: null,
            MaxUsesPerUser: 1,
            UsedCount: 0,
            ReservedCount: 0,
            IsVisible: true,
            Status: DiscountStatus.Active,
            StartsAt: null,
            EndsAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Scopes: new List<DiscountScopeDto>()
        );

        _mockDiscountService
            .Setup(s => s.CreateDiscountAsync(sellerId, false, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(discountDetail);

        // Act
        var result = await controller.CreateDiscount(request);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.Value.Should().Be(discountDetail);
    }

    [Fact]
    public async Task DeleteDiscount_ReturnsNoContent_WhenAuthorized()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var discountId = Guid.NewGuid();
        var controller = CreateController(sellerId, "seller");

        _mockDiscountService
            .Setup(s => s.DeleteDiscountAsync(sellerId, false, discountId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await controller.DeleteDiscount(discountId);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Endpoints_ReturnUnauthorized_WhenUserUnauthenticated()
    {
        // Arrange: controller without authenticated user
        var controller = CreateController(null, null);

        // Act
        var res1 = await controller.GetAvailableVouchers(null, null);
        var res2 = await controller.ValidateVoucher(new ValidateVoucherRequest("C", new List<Guid>()));
        var res3 = await controller.RecommendVouchers(new RecommendVouchersRequest(new List<Guid>()));

        // Assert
        res1.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        res2.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        res3.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }
}
