using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Cart;
using Commerce.Domain.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

namespace Commerce.UnitTests.Application;

public class CartServiceTests
{
    private readonly Mock<ICommerceDbContext> _mockDbContext = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();

    private CartService CreateService()
    {
        return new CartService(_mockDbContext.Object, _mockCatalogClient.Object);
    }

    [Fact]
    public async Task AddItemAsync_ShouldThrowException_WhenQuantityIsZeroOrNegative()
    {
        var service = CreateService();
        var request = new AddCartItemRequest(Guid.NewGuid(), 0);

        var act = () => service.AddItemAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "VAL_INVALID_INPUT");
    }

    [Fact]
    public async Task AddItemAsync_ShouldThrowException_WhenSkuNotFoundInCatalog()
    {
        var skuId = Guid.NewGuid();
        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogProductCheckResult?)null);

        var service = CreateService();
        var request = new AddCartItemRequest(skuId, 2);

        var act = () => service.AddItemAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CATALOG_SKU_NOT_FOUND");
    }

    [Fact]
    public async Task AddItemAsync_ShouldThrowException_WhenSkuIsInactiveInCatalog()
    {
        var skuId = Guid.NewGuid();
        var inactiveSku = new CatalogProductCheckResult(
            SkuId: skuId,
            ProductId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            ProductName: "Inactive Product",
            UnitPrice: 100000m,
            IsPurchasable: false,
            StockQuantity: 0,
            ErrorReason: "Product is out of stock"
        );

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveSku);

        var service = CreateService();
        var request = new AddCartItemRequest(skuId, 1);

        var act = () => service.AddItemAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CATALOG_SKU_INACTIVE");
    }

    [Fact]
    public async Task CalculateSelectedItemsAsync_ShouldReturnEmpty_WhenNoItemsSelected()
    {
        var service = CreateService();
        var request = new CalculateSelectedCartItemsRequest(new List<Guid>());

        var result = await service.CalculateSelectedItemsAsync(Guid.NewGuid(), request, CancellationToken.None);

        result.Should().NotBeNull();
        result.SelectedItemCount.Should().Be(0);
        result.SubtotalAmount.Should().Be(0m);
    }
}
