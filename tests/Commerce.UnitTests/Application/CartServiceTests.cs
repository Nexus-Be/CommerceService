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

    [Fact]
    public async Task ValidateCartAsync_ShouldReturnFulfillmentStockIssue_WhenWarehouseStockInsufficient()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, Guid.NewGuid(), Guid.NewGuid(), "Warehouse Item", 100000m, true, 100));

        // Warehouse chỉ còn 1 sản phẩm trong khi cart có 3
        _mockFulfillmentClient
            .Setup(f => f.BatchCheckStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SkuStockCheckResult>
            {
                [skuId] = new SkuStockCheckResult(skuId, IsAvailable: false, AvailableStock: 1, DisabledReason: "INSUFFICIENT_STOCK")
            });

        var service = CreateService();
        await service.AddItemAsync(buyerId, new AddCartItemRequest(skuId, 3), CancellationToken.None);

        // Act
        var result = await service.ValidateCartAsync(buyerId, new ValidateCartRequest(), CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.HasStockIssues.Should().BeTrue();
        result.Issues.Should().HaveCount(1);
        result.Issues[0].SkuId.Should().Be(skuId);
        result.Issues[0].IssueType.Should().Be("INSUFFICIENT_STOCK");
        result.Issues[0].AvailableStock.Should().Be(1);
        result.Issues[0].RequestedQuantity.Should().Be(3);
        result.Issues[0].SuggestedQuantity.Should().Be(1);
    }

    [Fact]
    public async Task ValidateCartAsync_ShouldUseParallelBatchLookup_WhenValidatingCart()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId1 = Guid.NewGuid();
        var skuId2 = Guid.NewGuid();

        var item1 = new CatalogProductCheckResult(skuId1, Guid.NewGuid(), Guid.NewGuid(), "Item 1", 100000m, true, 100);
        var item2 = new CatalogProductCheckResult(skuId2, Guid.NewGuid(), Guid.NewGuid(), "Item 2", 200000m, true, 100);

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item1);

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item2);

        _mockCatalogClient
            .Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CatalogProductCheckResult>
            {
                [skuId1] = item1,
                [skuId2] = item2
            });

        _mockFulfillmentClient
            .Setup(f => f.BatchCheckStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SkuStockCheckResult>
            {
                [skuId1] = new SkuStockCheckResult(skuId1, true, 50, null),
                [skuId2] = new SkuStockCheckResult(skuId2, true, 50, null)
            });

        var service = CreateService();
        await service.AddItemAsync(buyerId, new AddCartItemRequest(skuId1, 2), CancellationToken.None);
        await service.AddItemAsync(buyerId, new AddCartItemRequest(skuId2, 1), CancellationToken.None);

        // Act
        var result = await service.ValidateCartAsync(buyerId, new ValidateCartRequest(), CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
        _mockCatalogClient.Verify(c => c.GetSkuBatchDetailsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockFulfillmentClient.Verify(f => f.BatchCheckStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CalculateSelectedItemsAsync_ShouldExcludeOutOfStockItemsFromSubtotal()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuAvailable = Guid.NewGuid();
        var skuOutOfStock = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuAvailable, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuAvailable, Guid.NewGuid(), Guid.NewGuid(), "Item Available", 100000m, true, 100));

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuOutOfStock, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuOutOfStock, Guid.NewGuid(), Guid.NewGuid(), "Item Out of stock", 50000m, true, 100));

        _mockFulfillmentClient
            .Setup(f => f.BatchCheckStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SkuStockCheckResult>
            {
                [skuAvailable] = new SkuStockCheckResult(skuAvailable, IsAvailable: true, AvailableStock: 10, DisabledReason: null),
                [skuOutOfStock] = new SkuStockCheckResult(skuOutOfStock, IsAvailable: false, AvailableStock: 0, DisabledReason: "OUT_OF_STOCK")
            });

        var service = CreateService();
        var cart1 = await service.AddItemAsync(buyerId, new AddCartItemRequest(skuAvailable, 2), CancellationToken.None); // 200k
        var cart2 = await service.AddItemAsync(buyerId, new AddCartItemRequest(skuOutOfStock, 1), CancellationToken.None); // 50k (out of stock)

        var allItemIds = cart2.Items.Select(i => i.Id).ToList();

        // Act
        var calcResult = await service.CalculateSelectedItemsAsync(
            buyerId,
            new CalculateSelectedCartItemsRequest(allItemIds),
            CancellationToken.None);

        // Assert: 2 items được chọn nhưng tổng tiền chỉ tính cho món còn hàng (200k)
        calcResult.SelectedItemCount.Should().Be(2);
        calcResult.TotalQuantity.Should().Be(2);
        calcResult.SubtotalAmount.Should().Be(200000m);
        calcResult.Items.First(i => i.SkuId == skuOutOfStock).IsAvailable.Should().BeFalse();
        calcResult.Items.First(i => i.SkuId == skuOutOfStock).StockWarning.Should().Be("OUT_OF_STOCK");
    }

    [Fact]
    public async Task ChangeItemVariantAsync_ShouldThrowException_WhenWarehouseStockInsufficient()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var oldSkuId = Guid.NewGuid();
        var newSkuId = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(oldSkuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(oldSkuId, Guid.NewGuid(), Guid.NewGuid(), "Old SKU", 50000m, true, 100));

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(newSkuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(newSkuId, Guid.NewGuid(), Guid.NewGuid(), "New SKU", 60000m, true, 100));

        _mockCatalogClient
            .Setup(c => c.ValidateVariantSwitchAsync(oldSkuId, newSkuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogVariantSwitchResult(true, Guid.NewGuid(), Guid.NewGuid(), newSkuId, "New SKU", 60000m, true, 200));

        // Warehouse báo hết hàng cho newSkuId
        _mockFulfillmentClient
            .Setup(f => f.CheckStockAvailabilityAsync(newSkuId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService();
        var cart = await service.AddItemAsync(buyerId, new AddCartItemRequest(oldSkuId, 2), CancellationToken.None);
        var cartItemId = cart.Items.First().Id;

        // Act
        var act = () => service.ChangeItemVariantAsync(buyerId, cartItemId, new ChangeCartItemVariantRequest(newSkuId), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "INSUFFICIENT_STOCK");
    }

    [Fact]
    public async Task ChangeItemVariantAsync_ShouldThrowException_WhenCatalogRejectsVariantSwitchMismatch()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var oldSkuId = Guid.NewGuid();
        var newSkuId = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(oldSkuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(oldSkuId, Guid.NewGuid(), Guid.NewGuid(), "Old SKU", 50000m, true, 100));

        _mockCatalogClient
            .Setup(c => c.ValidateVariantSwitchAsync(oldSkuId, newSkuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogVariantSwitchResult(false, Guid.NewGuid(), Guid.NewGuid(), newSkuId, "Laptop Sku", 15000000m, true));

        var service = CreateService();
        var cart = await service.AddItemAsync(buyerId, new AddCartItemRequest(oldSkuId, 1), CancellationToken.None);
        var cartItemId = cart.Items.First().Id;

        // Act
        var act = () => service.ChangeItemVariantAsync(buyerId, cartItemId, new ChangeCartItemVariantRequest(newSkuId), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CATALOG_VARIANT_MISMATCH");
    }

    [Fact]
    public async Task ChangeItemVariantAsync_ShouldUpdateVariant_WhenCatalogSwitchValidationSucceeds()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var oldSkuId = Guid.NewGuid();
        var newSkuId = Guid.NewGuid();
        var parentProductId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(oldSkuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(oldSkuId, parentProductId, sellerId, "Áo Polo Đen M", 100000m, true, 100));

        _mockCatalogClient
            .Setup(c => c.ValidateVariantSwitchAsync(oldSkuId, newSkuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogVariantSwitchResult(true, parentProductId, sellerId, newSkuId, "Áo Polo Trắng L", 120000m, true, 250));

        _mockFulfillmentClient
            .Setup(f => f.CheckStockAvailabilityAsync(newSkuId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();
        var cart = await service.AddItemAsync(buyerId, new AddCartItemRequest(oldSkuId, 2), CancellationToken.None);
        var cartItemId = cart.Items.First().Id;

        // Act
        var updatedCart = await service.ChangeItemVariantAsync(buyerId, cartItemId, new ChangeCartItemVariantRequest(newSkuId), CancellationToken.None);

        // Assert
        var item = updatedCart.Items.First(i => i.Id == cartItemId);
        item.SkuId.Should().Be(newSkuId);
        item.ProductName.Should().Be("Áo Polo Trắng L");
        item.UnitPriceSnapshot.Should().Be(120000m);
        updatedCart.SubtotalAmount.Should().Be(240000m);
    }

    [Fact]
    public async Task GetActiveCart_WhenProductInactiveInCatalog_ShouldSet_IsAvailableFalse_And_PRODUCT_INACTIVE()
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
        await service.AddItemAsync(buyerId, new AddCartItemRequest(skuId, 1), CancellationToken.None);

        // Catalog returns inactive for batch check
        _mockCatalogClient
            .Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CatalogProductCheckResult>
            {
                [skuId] = new CatalogProductCheckResult(skuId, productId, sellerId, "Test Product", 50000m, false, 100)
            });

        _mockFulfillmentClient
            .Setup(f => f.BatchCheckStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SkuStockCheckResult>
            {
                [skuId] = new SkuStockCheckResult(skuId, true, 10, null)
            });

        // Act
        var cart = await service.GetActiveCartByBuyerIdAsync(buyerId, CancellationToken.None);

        // Assert
        cart.Items.Should().HaveCount(1);
        var cartItem = cart.Items.First();
        cartItem.IsAvailable.Should().BeFalse();
        cartItem.DisabledReason.Should().Be("PRODUCT_INACTIVE");
    }

    [Fact]
    public async Task GetActiveCart_WhenProductNotFoundInCatalog_ShouldSet_IsAvailableFalse_And_PRODUCT_NOT_FOUND()
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
        await service.AddItemAsync(buyerId, new AddCartItemRequest(skuId, 1), CancellationToken.None);

        // Catalog returns empty map (SKU deleted/not found)
        _mockCatalogClient
            .Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CatalogProductCheckResult>());

        _mockFulfillmentClient
            .Setup(f => f.BatchCheckStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SkuStockCheckResult>
            {
                [skuId] = new SkuStockCheckResult(skuId, true, 10, null)
            });

        // Act
        var cart = await service.GetActiveCartByBuyerIdAsync(buyerId, CancellationToken.None);

        // Assert
        cart.Items.Should().HaveCount(1);
        var cartItem = cart.Items.First();
        cartItem.IsAvailable.Should().BeFalse();
        cartItem.DisabledReason.Should().Be("PRODUCT_NOT_FOUND");
    }

    [Fact]
    public async Task GetActiveCart_WhenInactiveAndOutOfStock_ShouldPrioritize_PRODUCT_INACTIVE()
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
        await service.AddItemAsync(buyerId, new AddCartItemRequest(skuId, 1), CancellationToken.None);

        // Both Inactive AND Out of stock
        _mockCatalogClient
            .Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CatalogProductCheckResult>
            {
                [skuId] = new CatalogProductCheckResult(skuId, productId, sellerId, "Test Product", 50000m, false, 0)
            });

        _mockFulfillmentClient
            .Setup(f => f.BatchCheckStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SkuStockCheckResult>
            {
                [skuId] = new SkuStockCheckResult(skuId, false, 0, "OUT_OF_STOCK")
            });

        // Act
        var cart = await service.GetActiveCartByBuyerIdAsync(buyerId, CancellationToken.None);

        // Assert: PRODUCT_INACTIVE must take precedence over OUT_OF_STOCK
        cart.Items.Should().HaveCount(1);
        var cartItem = cart.Items.First();
        cartItem.IsAvailable.Should().BeFalse();
        cartItem.DisabledReason.Should().Be("PRODUCT_INACTIVE");
    }

    [Fact]
    public async Task ValidateCartAsync_WhenPriceChangedAndProductInactive_ShouldPersistPriceSnapshotAndFlagIssues()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sku1 = Guid.NewGuid();
        var sku2 = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(sku1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(sku1, Guid.NewGuid(), sellerId, "Item 1", 100000m, true, 100));

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(sku2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(sku2, Guid.NewGuid(), sellerId, "Item 2", 50000m, true, 100));

        _mockFulfillmentClient
            .Setup(f => f.CheckStockAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.BatchCheckStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SkuStockCheckResult>
            {
                [sku1] = new SkuStockCheckResult(sku1, true, 50, null),
                [sku2] = new SkuStockCheckResult(sku2, true, 50, null)
            });

        var service = CreateService();
        await service.AddItemAsync(buyerId, new AddCartItemRequest(sku1, 2), CancellationToken.None);
        await service.AddItemAsync(buyerId, new AddCartItemRequest(sku2, 1), CancellationToken.None);

        // Catalog returns price change for sku1 (100k -> 120k) and inactive for sku2
        _mockCatalogClient
            .Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CatalogProductCheckResult>
            {
                [sku1] = new CatalogProductCheckResult(sku1, Guid.NewGuid(), sellerId, "Item 1", 120000m, true, 100),
                [sku2] = new CatalogProductCheckResult(sku2, Guid.NewGuid(), sellerId, "Item 2", 50000m, false, 100)
            });

        // Act
        var result = await service.ValidateCartAsync(buyerId, new ValidateCartRequest(), CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.HasPriceChanges.Should().BeTrue();
        result.Issues.Should().Contain(i => i.SkuId == sku1 && i.IssueType == "PRICE_CHANGED" && i.NewPrice == 120000m && i.OldPrice == 100000m);
        result.Issues.Should().Contain(i => i.SkuId == sku2 && i.IssueType == "PRODUCT_INACTIVE");

        // Verify direct database persistence of new snapshot price
        var dbCart = await _dbContext.Carts.Include(c => c.Items).FirstAsync(c => c.BuyerId == buyerId);
        var dbItem1 = dbCart.Items.First(i => i.SkuId == sku1);
        dbItem1.UnitPriceSnapshot.Should().Be(120000m);
        dbCart.SubtotalAmount.Should().Be(2 * 120000m + 1 * 50000m);
    }
}
