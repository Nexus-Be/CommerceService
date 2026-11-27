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

    [Fact]
    public async Task ValidateVoucherAsync_ShouldThrowInvalidInput_WhenCartItemBelongsToAnotherBuyer()
    {
        // Arrange
        var ownerBuyerId = Guid.NewGuid();
        var attackerBuyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            BuyerId = ownerBuyerId,
            Status = CartStatus.Active
        };
        var cartItem = new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            ProductId = Guid.NewGuid(),
            SkuId = Guid.NewGuid(),
            SellerId = sellerId,
            ProductNameSnapshot = "Victim Item",
            Quantity = 1,
            UnitPriceSnapshot = 500000m
        };
        cart.Items.Add(cartItem);
        _db.Carts.Add(cart);
        _db.CartItems.Add(cartItem);

        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SAVE50",
            Name = "Save 50k",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 50000m,
            MinOrderAmount = 100000m,
            Status = DiscountStatus.Active,
            IsVisible = true
        };
        _db.DiscountCodes.Add(voucher);
        await _db.SaveChangesAsync();

        var request = new ValidateVoucherRequest("SAVE50", new[] { cartItem.Id });

        // Act
        var act = () => _service.ValidateVoucherAsync(attackerBuyerId, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("VAL_INVALID_INPUT");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ValidateVoucherAsync_ShouldReturnOutOfStock_WhenTotalUsesReached()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "LIMITED50",
            Name = "Limited 50k",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 50000m,
            MinOrderAmount = 100000m,
            MaxUsesTotal = 5,
            UsedCount = 3,
            ReservedCount = 2,
            Status = DiscountStatus.Active,
            IsVisible = true
        };
        _db.DiscountCodes.Add(voucher);
        await _db.SaveChangesAsync();

        var request = new ValidateVoucherRequest("LIMITED50", new[] { Guid.NewGuid() });

        // Act
        var result = await _service.ValidateVoucherAsync(buyerId, request);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.IneligibleReason.Should().Be("VOUCHER_OUT_OF_STOCK");
        result.Message.Should().Be("Voucher has reached its total usage limit.");
        result.EstimatedDiscountAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ValidateVoucherAsync_ShouldReturnUsageLimitExceeded_WhenUserLimitReached()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "ONETIME10",
            Name = "One Time 10k",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            MinOrderAmount = 50000m,
            MaxUsesTotal = 100,
            MaxUsesPerUser = 1,
            UsedCount = 5,
            ReservedCount = 0,
            Status = DiscountStatus.Active,
            IsVisible = true
        };
        _db.DiscountCodes.Add(voucher);

        var existingUsage = new DiscountCodeUsage
        {
            Id = Guid.NewGuid(),
            DiscountCodeId = voucher.Id,
            UserId = buyerId,
            Status = DiscountUsageStatus.Reserved,
            DiscountAmount = 10000m
        };
        _db.DiscountCodeUsages.Add(existingUsage);
        await _db.SaveChangesAsync();

        var request = new ValidateVoucherRequest("ONETIME10", new[] { Guid.NewGuid() });

        // Act
        var result = await _service.ValidateVoucherAsync(buyerId, request);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.IneligibleReason.Should().Be("VOUCHER_USAGE_LIMIT_EXCEEDED");
        result.Message.Should().Be("You have reached the usage limit for this voucher.");
        result.EstimatedDiscountAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ValidateVoucherAsync_ShouldReturnVoucherMetadata_WhenValid()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var cart = new Cart { Id = Guid.NewGuid(), BuyerId = buyerId, Status = CartStatus.Active };
        var cartItem = new CartItem
        {
            Id = Guid.NewGuid(), CartId = cart.Id, ProductId = Guid.NewGuid(), SkuId = Guid.NewGuid(),
            SellerId = sellerId, ProductNameSnapshot = "Shirt", Quantity = 1, UnitPriceSnapshot = 200000m
        };
        cart.Items.Add(cartItem);
        _db.Carts.Add(cart);
        _db.CartItems.Add(cartItem);
        _db.DiscountCodes.Add(new DiscountCode
        {
            Id = Guid.NewGuid(), Code = "SHOP20", Name = "Shop 20k",
            ScopeType = DiscountScopeType.Shop, OwnerSellerId = sellerId,
            DiscountType = DiscountType.FixedAmount, DiscountValue = 20000m,
            MinOrderAmount = 100000m, MaxDiscountAmount = 50000m,
            Status = DiscountStatus.Active, IsVisible = false
        });
        await _db.SaveChangesAsync();

        var realService = new DiscountService(_db, _mockUserClient.Object, _mockCatalogClient.Object, new VoucherCalculationEngine());
        var res = await realService.ValidateVoucherAsync(buyerId, new ValidateVoucherRequest("SHOP20", new[] { cartItem.Id }));

        res.IsValid.Should().BeTrue();
        res.OwnerSellerId.Should().Be(sellerId);
        res.DiscountType.Should().Be(DiscountType.FixedAmount);
        res.DiscountValue.Should().Be(20000m);
        res.MinOrderAmount.Should().Be(100000m);
        res.MaxDiscountAmount.Should().Be(50000m);
    }

    [Fact]
    public async Task RecommendVouchersAsync_ShouldSkipExhaustedAndUserLimitVouchers_ExcludeSystemOnCod_AndComputeRemainingSystemBase()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        var cart = new Cart { Id = Guid.NewGuid(), BuyerId = buyerId, Status = CartStatus.Active };
        var cartItem = new CartItem
        {
            Id = Guid.NewGuid(), CartId = cart.Id, ProductId = productId, SkuId = skuId,
            SellerId = sellerId, ProductNameSnapshot = "Jacket", Quantity = 1, UnitPriceSnapshot = 500000m
        };
        cart.Items.Add(cartItem);
        _db.Carts.Add(cart);
        _db.CartItems.Add(cartItem);

        // Exhausted total uses -> must be skipped
        var exhaustedTotal = new DiscountCode
        {
            Id = Guid.NewGuid(), Code = "EXHAUSTED_TOT", Name = "Exhausted",
            ScopeType = DiscountScopeType.Shop, OwnerSellerId = sellerId,
            DiscountType = DiscountType.FixedAmount, DiscountValue = 200000m,
            MaxUsesTotal = 1, UsedCount = 1, Status = DiscountStatus.Active, IsVisible = true
        };
        // Exhausted per-user uses -> must be skipped
        var usedUpByUser = new DiscountCode
        {
            Id = Guid.NewGuid(), Code = "USED_BY_ME", Name = "Used by me",
            ScopeType = DiscountScopeType.Shop, OwnerSellerId = sellerId,
            DiscountType = DiscountType.FixedAmount, DiscountValue = 150000m,
            MaxUsesTotal = 100, MaxUsesPerUser = 1, Status = DiscountStatus.Active, IsVisible = true
        };
        // Product voucher scoped by ProductId -> 50k off (better than Shop 30k)
        var prodVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(), Code = "PROD50", Name = "Prod 50k",
            ScopeType = DiscountScopeType.Product, OwnerSellerId = sellerId,
            DiscountType = DiscountType.FixedAmount, DiscountValue = 50000m,
            MinOrderAmount = 100000m, Status = DiscountStatus.Active, IsVisible = true,
            Scopes = new List<DiscountCodeScope>
            {
                new() { Id = Guid.NewGuid(), DiscountCodeId = Guid.NewGuid(), ScopeRefType = DiscountScopeRefType.Product, ScopeRefId = productId }
            }
        };
        var shopVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(), Code = "SHOP30", Name = "Shop 30k",
            ScopeType = DiscountScopeType.Shop, OwnerSellerId = sellerId,
            DiscountType = DiscountType.FixedAmount, DiscountValue = 30000m,
            MinOrderAmount = 100000m, Status = DiscountStatus.Active, IsVisible = true
        };
        // System voucher requiring remaining base >= 400,000 (500,000 - 50,000 = 450,000 >= 400,000)
        var sysVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(), Code = "SYS40", Name = "System 40k",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount, DiscountValue = 40000m,
            MinOrderAmount = 400000m, Status = DiscountStatus.Active, IsVisible = true
        };

        _db.DiscountCodes.AddRange(exhaustedTotal, usedUpByUser, prodVoucher, shopVoucher, sysVoucher);
        _db.DiscountCodeUsages.Add(new DiscountCodeUsage
        {
            Id = Guid.NewGuid(), DiscountCodeId = usedUpByUser.Id, UserId = buyerId,
            Status = DiscountUsageStatus.Used, DiscountAmount = 150000m
        });
        await _db.SaveChangesAsync();

        var realService = new DiscountService(_db, _mockUserClient.Object, _mockCatalogClient.Object, new VoucherCalculationEngine());

        var walletRec = await realService.RecommendVouchersAsync(buyerId, new RecommendVouchersRequest(new[] { cartItem.Id }, PaymentMethod.Wallet));
        walletRec.RecommendedVoucherCodes.Should().BeEquivalentTo(new[] { "PROD50", "SYS40" });
        walletRec.SavingsBreakdown.TotalSavings.Should().Be(90000m);

        var codRec = await realService.RecommendVouchersAsync(buyerId, new RecommendVouchersRequest(new[] { cartItem.Id }, PaymentMethod.Cod));
        codRec.RecommendedVoucherCodes.Should().BeEquivalentTo(new[] { "PROD50" });
        codRec.SavingsBreakdown.SystemDiscount.Should().Be(0m);
        codRec.SavingsBreakdown.TotalSavings.Should().Be(50000m);
    }

    [Fact]
    public async Task GetAvailableVouchersAsync_ShouldMarkUserLimitExceededAsIneligible()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var cart = new Cart { Id = Guid.NewGuid(), BuyerId = buyerId, Status = CartStatus.Active };
        var cartItem = new CartItem
        {
            Id = Guid.NewGuid(), CartId = cart.Id, ProductId = Guid.NewGuid(), SkuId = Guid.NewGuid(),
            SellerId = sellerId, ProductNameSnapshot = "Book", Quantity = 1, UnitPriceSnapshot = 200000m
        };
        cart.Items.Add(cartItem);
        _db.Carts.Add(cart);
        _db.CartItems.Add(cartItem);

        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(), Code = "ONCE_ONLY", Name = "Once only",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount, DiscountValue = 20000m,
            MaxUsesPerUser = 1, Status = DiscountStatus.Active, IsVisible = true
        };
        _db.DiscountCodes.Add(voucher);
        _db.DiscountCodeUsages.Add(new DiscountCodeUsage
        {
            Id = Guid.NewGuid(), DiscountCodeId = voucher.Id, UserId = buyerId,
            Status = DiscountUsageStatus.Used, DiscountAmount = 20000m
        });
        await _db.SaveChangesAsync();

        var realService = new DiscountService(_db, _mockUserClient.Object, _mockCatalogClient.Object, new VoucherCalculationEngine());
        var page = await realService.GetAvailableVouchersAsync(buyerId, null, new[] { cartItem.Id }, 1, 20);

        var item = page.Items.Should().ContainSingle(x => x.Code == "ONCE_ONLY").Subject;
        item.IsEligible.Should().BeFalse();
        item.IneligibleReason.Should().Be("You have reached the usage limit for this voucher.");
        item.EstimatedDiscountAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ValidateVoucherAsync_ShouldReturnSpecificIneligibleReason_WhenExpiredOrNotStartedOrMinOrderNotMet()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var cart = new Cart { Id = Guid.NewGuid(), BuyerId = buyerId, Status = CartStatus.Active };
        var cartItem = new CartItem
        {
            Id = Guid.NewGuid(), CartId = cart.Id, ProductId = Guid.NewGuid(), SkuId = Guid.NewGuid(),
            SellerId = sellerId, ProductNameSnapshot = "Tea", Quantity = 1, UnitPriceSnapshot = 100000m
        };
        cart.Items.Add(cartItem);
        _db.Carts.Add(cart);
        _db.CartItems.Add(cartItem);

        _db.DiscountCodes.AddRange(
            new DiscountCode
            {
                Id = Guid.NewGuid(), Code = "EXP_V", Name = "Expired", ScopeType = DiscountScopeType.Shop, OwnerSellerId = sellerId,
                DiscountType = DiscountType.FixedAmount, DiscountValue = 10000m, EndsAt = DateTimeOffset.UtcNow.AddHours(-1),
                Status = DiscountStatus.Active, IsVisible = true
            },
            new DiscountCode
            {
                Id = Guid.NewGuid(), Code = "FUT_V", Name = "Future", ScopeType = DiscountScopeType.Shop, OwnerSellerId = sellerId,
                DiscountType = DiscountType.FixedAmount, DiscountValue = 10000m, StartsAt = DateTimeOffset.UtcNow.AddHours(2),
                Status = DiscountStatus.Active, IsVisible = true
            },
            new DiscountCode
            {
                Id = Guid.NewGuid(), Code = "MIN_V", Name = "Min 500k", ScopeType = DiscountScopeType.Shop, OwnerSellerId = sellerId,
                DiscountType = DiscountType.FixedAmount, DiscountValue = 10000m, MinOrderAmount = 500000m,
                Status = DiscountStatus.Active, IsVisible = true
            });
        await _db.SaveChangesAsync();

        var realService = new DiscountService(_db, _mockUserClient.Object, _mockCatalogClient.Object, new VoucherCalculationEngine());

        var expRes = await realService.ValidateVoucherAsync(buyerId, new ValidateVoucherRequest("EXP_V", new[] { cartItem.Id }));
        expRes.IsValid.Should().BeFalse();
        expRes.IneligibleReason.Should().Be("VOUCHER_EXPIRED");

        var futRes = await realService.ValidateVoucherAsync(buyerId, new ValidateVoucherRequest("FUT_V", new[] { cartItem.Id }));
        futRes.IsValid.Should().BeFalse();
        futRes.IneligibleReason.Should().Be("VOUCHER_NOT_STARTED");

        var minRes = await realService.ValidateVoucherAsync(buyerId, new ValidateVoucherRequest("MIN_V", new[] { cartItem.Id }));
        minRes.IsValid.Should().BeFalse();
        minRes.IneligibleReason.Should().Be("VOUCHER_MIN_ORDER_NOT_MET");
    }
}
