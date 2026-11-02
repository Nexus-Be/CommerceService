namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Cart;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Infrastructure.Persistence;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class CartServiceTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();

    public CartServiceTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _mockFulfillmentClient
            .Setup(f => f.CheckStockAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private CartService CreateService()
    {
        return new CartService(_dbContext, _mockCatalogClient.Object, _mockFulfillmentClient.Object);
    }

    // =========================================================================
    // NHÓM A.1 & A.2: THÊM MÓN & VALIDATION CATALOG / KHO
    // =========================================================================

    [Fact]
    public async Task AddItemAsync_ShouldCreateCartAndAddItem_WhenValid()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, productId, sellerId, "Test Product", 50000m, true, 100));

        var service = CreateService();
        var request = new AddCartItemRequest(skuId, 2, productId);

        // Act
        var result = await service.AddItemAsync(buyerId, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.BuyerId.Should().Be(buyerId);
        result.ItemCount.Should().Be(1);
        result.SubtotalAmount.Should().Be(100000m);
        result.Items.Should().HaveCount(1);
        result.Items[0].SkuId.Should().Be(skuId);
        result.Items[0].Quantity.Should().Be(2);
        result.Items[0].UnitPriceSnapshot.Should().Be(50000m);
        result.Items[0].LineTotal.Should().Be(100000m);

        // Verify Database
        var dbCart = await _dbContext.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.BuyerId == buyerId);
        dbCart.Should().NotBeNull();
        dbCart!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddItemAsync_ShouldAccumulateQuantity_WhenDuplicateSkuAdded()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, productId, sellerId, "Test Product", 50000m, true, 100));

        var service = CreateService();

        // Act 1: Thêm lần đầu 2 món
        await service.AddItemAsync(buyerId, new AddCartItemRequest(skuId, 2, productId), CancellationToken.None);

        // Act 2: Thêm tiếp 3 món cùng SKU
        var result = await service.AddItemAsync(buyerId, new AddCartItemRequest(skuId, 3, productId), CancellationToken.None);

        // Assert
        result.ItemCount.Should().Be(1); // Vẫn chỉ là 1 distinct item
        result.Items.Should().HaveCount(1);
        result.Items[0].Quantity.Should().Be(5); // 2 + 3 = 5
        result.SubtotalAmount.Should().Be(250000m); // 5 * 50,000 = 250,000
    }

    [Fact]
    public async Task AddItemAsync_ShouldThrowException_WhenQuantityIsZeroOrNegative()
    {
        // Arrange
        var service = CreateService();
        var request = new AddCartItemRequest(Guid.NewGuid(), 0);

        // Act
        var act = () => service.AddItemAsync(Guid.NewGuid(), request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "VAL_INVALID_INPUT");
    }

    [Fact]
    public async Task AddItemAsync_ShouldThrowException_WhenSkuNotFoundInCatalog()
    {
        // Arrange
        var skuId = Guid.NewGuid();
        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogProductCheckResult?)null);

        var service = CreateService();
        var request = new AddCartItemRequest(skuId, 2);

        // Act
        var act = () => service.AddItemAsync(Guid.NewGuid(), request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CATALOG_SKU_NOT_FOUND");
    }

    [Fact]
    public async Task AddItemAsync_ShouldThrowException_WhenSkuIsInactiveInCatalog()
    {
        // Arrange
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

        // Act
        var act = () => service.AddItemAsync(Guid.NewGuid(), request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CATALOG_SKU_INACTIVE");
    }

    [Fact]
    public async Task AddItemAsync_ShouldThrowException_WhenStockIsInsufficientInFulfillment()
    {
        // Arrange
        var skuId = Guid.NewGuid();
        var activeSku = new CatalogProductCheckResult(
            SkuId: skuId,
            ProductId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            ProductName: "Available Product",
            UnitPrice: 100000m,
            IsPurchasable: true,
            StockQuantity: 10
        );

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSku);

        _mockFulfillmentClient
            .Setup(f => f.CheckStockAvailabilityAsync(skuId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService();
        var request = new AddCartItemRequest(skuId, 5);

        // Act
        var act = () => service.AddItemAsync(Guid.NewGuid(), request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "INSUFFICIENT_STOCK");
    }

    // =========================================================================
    // NHÓM A.3: XÓA MÓN, XÓA TOÀN BỘ & TÍNH TỔNG TIỀN CHÍNH XÁC
    // =========================================================================

    [Fact]
    public async Task RemoveItemAsync_ShouldRemoveItemAndRecalculateTotals()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sku1 = Guid.NewGuid();
        var sku2 = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(sku1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(sku1, Guid.NewGuid(), Guid.NewGuid(), "Item 1", 50000m, true, 100));

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(sku2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(sku2, Guid.NewGuid(), Guid.NewGuid(), "Item 2", 30000m, true, 100));

        var service = CreateService();
        await service.AddItemAsync(buyerId, new AddCartItemRequest(sku1, 2), CancellationToken.None);
        var cartDto = await service.AddItemAsync(buyerId, new AddCartItemRequest(sku2, 1), CancellationToken.None);

        cartDto.ItemCount.Should().Be(2);
        cartDto.SubtotalAmount.Should().Be(130000m); // (2*50k) + (1*30k)

        var itemToRemove = cartDto.Items.First(i => i.SkuId == sku1);

        // Act: Xóa món 1
        var updatedCart = await service.RemoveItemAsync(buyerId, itemToRemove.Id, CancellationToken.None);

        // Assert
        updatedCart.ItemCount.Should().Be(1);
        updatedCart.SubtotalAmount.Should().Be(30000m);
        updatedCart.Items.Should().HaveCount(1);
        updatedCart.Items[0].SkuId.Should().Be(sku2);
    }

    [Fact]
    public async Task BulkRemoveOrClearAsync_ShouldClearAllItems_WhenClearAllIsTrue()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sku1 = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(sku1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(sku1, Guid.NewGuid(), Guid.NewGuid(), "Item 1", 50000m, true, 100));

        var service = CreateService();
        await service.AddItemAsync(buyerId, new AddCartItemRequest(sku1, 3), CancellationToken.None);

        // Act: Clear all
        var clearedCart = await service.BulkRemoveOrClearAsync(buyerId, new BulkRemoveCartItemsRequest(null, ClearAll: true), CancellationToken.None);

        // Assert
        clearedCart.ItemCount.Should().Be(0);
        clearedCart.SubtotalAmount.Should().Be(0m);
        clearedCart.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculateSelectedItemsAsync_ShouldReturnCorrectSubtotal_ForSelectedSubset()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sku1 = Guid.NewGuid();
        var sku2 = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(sku1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(sku1, Guid.NewGuid(), Guid.NewGuid(), "Item 1", 100000m, true, 100));

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(sku2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(sku2, Guid.NewGuid(), Guid.NewGuid(), "Item 2", 50000m, true, 100));

        var service = CreateService();
        await service.AddItemAsync(buyerId, new AddCartItemRequest(sku1, 2), CancellationToken.None); // 200k
        var cartDto = await service.AddItemAsync(buyerId, new AddCartItemRequest(sku2, 3), CancellationToken.None); // 150k

        var selectedItemId = cartDto.Items.First(i => i.SkuId == sku1).Id;

        // Act: Chỉ tính tiền món 1
        var calcResult = await service.CalculateSelectedItemsAsync(
            buyerId,
            new CalculateSelectedCartItemsRequest(new List<Guid> { selectedItemId }),
            CancellationToken.None);

        // Assert
        calcResult.SelectedItemCount.Should().Be(1);
        calcResult.TotalQuantity.Should().Be(2);
        calcResult.SubtotalAmount.Should().Be(200000m);
    }
}
