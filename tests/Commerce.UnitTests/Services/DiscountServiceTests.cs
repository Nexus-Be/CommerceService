namespace Commerce.UnitTests.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Discount;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;

public class DiscountServiceTests
{
    private readonly TestCommerceDbContext _db;
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IVoucherCalculationEngine> _mockCalculationEngine = new();
    private readonly DiscountService _service;

    public DiscountServiceTests()
    {
        _db = TestDbContextFactory.CreateInMemoryDbContext();
        _mockUserClient
            .Setup(u => u.CheckBuyerPermissionAsync(It.IsAny<Guid>(), "manage_voucher", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserPermissionCheckResult(true, null, null));

        _service = new DiscountService(
            _db,
            _mockUserClient.Object,
            _mockCatalogClient.Object,
            _mockCalculationEngine.Object);
    }

    [Fact]
    public async Task CreateDiscountAsync_ShouldThrowVoucherSkuAuctionNotAllowed_WhenSkuIsAuctionItem()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var request = new CreateDiscountRequest(
            Code: "AUCTION10",
            Name: "10% off Auction",
            ScopeType: DiscountScopeType.Product,
            OwnerSellerId: sellerId,
            DiscountType: DiscountType.Percentage,
            DiscountValue: 10,
            MinOrderAmount: 0m,
            MaxDiscountAmount: null,
            MaxUsesTotal: 100,
            MaxUsesPerUser: 1,
            IsVisible: true,
            StartsAt: null,
            EndsAt: null,
            Scopes: new List<CreateDiscountScopeInput>
            {
                new(DiscountScopeRefType.Sku, skuId)
            });

        _mockCatalogClient
            .Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CatalogProductCheckResult>
            {
                [skuId] = new CatalogProductCheckResult(
                    SkuId: skuId,
                    ProductId: Guid.NewGuid(),
                    SellerId: sellerId,
                    ProductName: "Rare Painting",
                    UnitPrice: 1000000m,
                    IsPurchasable: true,
                    SaleType: "Auction")
            });

        // Act
        var act = () => _service.CreateDiscountAsync(sellerId, false, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Where(e => e.ErrorCode == "VOUCHER_SKU_AUCTION_NOT_ALLOWED" && e.StatusCode == 400);
        ex.WithMessage("Voucher cannot be applied to auction products.");
    }

    [Fact]
    public async Task CreateDiscountAsync_ShouldThrowCatalogSkuInactive_WhenSkuIsNotPurchasable()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var request = new CreateDiscountRequest(
            Code: "PROD10",
            Name: "10% off Product",
            ScopeType: DiscountScopeType.Product,
            OwnerSellerId: sellerId,
            DiscountType: DiscountType.Percentage,
            DiscountValue: 10,
            MinOrderAmount: 0m,
            MaxDiscountAmount: null,
            MaxUsesTotal: 100,
            MaxUsesPerUser: 1,
            IsVisible: true,
            StartsAt: null,
            EndsAt: null,
            Scopes: new List<CreateDiscountScopeInput>
            {
                new(DiscountScopeRefType.Sku, skuId)
            });

        _mockCatalogClient
            .Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CatalogProductCheckResult>
            {
                [skuId] = new CatalogProductCheckResult(
                    SkuId: skuId,
                    ProductId: Guid.NewGuid(),
                    SellerId: sellerId,
                    ProductName: "Out of Stock Shirt",
                    UnitPrice: 200000m,
                    IsPurchasable: false,
                    SaleType: "Regular")
            });

        // Act
        var act = () => _service.CreateDiscountAsync(sellerId, false, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Where(e => e.ErrorCode == "CATALOG_SKU_INACTIVE" && e.StatusCode == 400);
        ex.WithMessage("Product 'Out of Stock Shirt' is no longer available for purchase.");
    }

    [Fact]
    public async Task CreateDiscountAsync_ShouldThrowVoucherSellerSkuForbidden_WhenSkuBelongsToAnotherSeller()
    {
        // Arrange
        var currentSellerId = Guid.NewGuid();
        var anotherSellerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var request = new CreateDiscountRequest(
            Code: "PROD20",
            Name: "20% off Other Shop Product",
            ScopeType: DiscountScopeType.Product,
            OwnerSellerId: currentSellerId,
            DiscountType: DiscountType.Percentage,
            DiscountValue: 20,
            MinOrderAmount: 0m,
            MaxDiscountAmount: null,
            MaxUsesTotal: 100,
            MaxUsesPerUser: 1,
            IsVisible: true,
            StartsAt: null,
            EndsAt: null,
            Scopes: new List<CreateDiscountScopeInput>
            {
                new(DiscountScopeRefType.Sku, skuId)
            });

        _mockCatalogClient
            .Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CatalogProductCheckResult>
            {
                [skuId] = new CatalogProductCheckResult(
                    SkuId: skuId,
                    ProductId: Guid.NewGuid(),
                    SellerId: anotherSellerId,
                    ProductName: "Competitor Watch",
                    UnitPrice: 5000000m,
                    IsPurchasable: true,
                    SaleType: "Regular")
            });

        // Act
        var act = () => _service.CreateDiscountAsync(currentSellerId, false, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Where(e => e.ErrorCode == "VOUCHER_SELLER_SKU_FORBIDDEN" && e.StatusCode == 403);
    }

    [Fact]
    public async Task RecommendVouchersAsync_ShouldPickSingleBestVoucherPerShop_WithMaxDiscount()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        _mockCalculationEngine
            .Setup(e => e.CalculateRawDiscount(It.IsAny<decimal>(), It.IsAny<DiscountCode>()))
            .Returns<decimal, DiscountCode>((baseAmount, voucher) => new VoucherCalculationEngine().CalculateRawDiscount(baseAmount, voucher));

        _mockCalculationEngine
            .Setup(e => e.EvaluateAndApplyVouchers(It.IsAny<EvaluateVouchersRequest>()))
            .Returns<EvaluateVouchersRequest>(req => new VoucherCalculationEngine().EvaluateAndApplyVouchers(req));

        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = CartStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var cartItem = new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            ProductId = Guid.NewGuid(),
            SkuId = skuId,
            SellerId = sellerId,
            ProductNameSnapshot = "Test Item",
            Quantity = 2,
            UnitPriceSnapshot = 200000m // Total: 400,000 VND
        };
        cart.Items.Add(cartItem);
        _db.Carts.Add(cart);
        _db.CartItems.Add(cartItem);

        // Voucher 1: Product voucher 30k off
        var prodVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "PROD30K",
            Name = "Product 30k",
            ScopeType = DiscountScopeType.Product,
            OwnerSellerId = sellerId,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 30000m,
            MinOrderAmount = 100000m,
            Status = DiscountStatus.Active,
            IsVisible = true,
            Scopes = new List<DiscountCodeScope>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    DiscountCodeId = Guid.NewGuid(),
                    ScopeRefType = DiscountScopeRefType.Sku,
                    ScopeRefId = skuId
                }
            }
        };

        // Voucher 2: Shop voucher 50k off (Better!)
        var shopVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SHOP50K",
            Name = "Shop 50k",
            ScopeType = DiscountScopeType.Shop,
            OwnerSellerId = sellerId,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 50000m,
            MinOrderAmount = 200000m,
            Status = DiscountStatus.Active,
            IsVisible = true
        };

        _db.DiscountCodes.AddRange(prodVoucher, shopVoucher);
        await _db.SaveChangesAsync();

        var request = new RecommendVouchersRequest(new List<Guid> { cartItem.Id });

        // Act
        var response = await _service.RecommendVouchersAsync(buyerId, request);

        // Assert
        response.RecommendedVoucherCodes.Should().ContainSingle()
            .Which.Should().Be("SHOP50K");
        response.SavingsBreakdown.ShopDiscount.Should().Be(50000m);
        response.SavingsBreakdown.SystemDiscount.Should().Be(0m);
        response.SavingsBreakdown.TotalSavings.Should().Be(50000m);
    }
}
