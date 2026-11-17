namespace Commerce.UnitTests.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Commerce.Application.Services.Discount;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;

public class VoucherCalculationEngineTests
{
    private readonly VoucherCalculationEngine _engine = new();

    [Fact]
    public void Case1_StackingShopAndSystem_ExactTotalIntegerVndSumMatch()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var item = new CalculationItemInput(
            CartItemId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            SkuId: Guid.NewGuid(),
            SellerId: sellerId,
            ProductName: "Test Product",
            Quantity: 2,
            UnitPrice: 500000m // Total: 1,000,000 VND
        );

        var shopVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SHOP50K",
            Name = "Shop 50K Off",
            ScopeType = DiscountScopeType.Shop,
            OwnerSellerId = sellerId,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 50000m,
            MinOrderAmount = 200000m,
            Status = DiscountStatus.Active
        };

        var systemVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SYS10PCT",
            Name = "System 10% Off",
            ScopeType = DiscountScopeType.System,
            OwnerSellerId = null,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            MinOrderAmount = 300000m,
            Status = DiscountStatus.Active
        };

        var request = new EvaluateVouchersRequest(
            Items: new List<CalculationItemInput> { item },
            AvailableVouchers: new List<DiscountCode> { shopVoucher, systemVoucher },
            PlatformMarkupPercent: 5.0m,
            SellerShippingFees: new Dictionary<Guid, decimal> { [sellerId] = 30000m }
        );

        // Act
        var result = _engine.EvaluateAndApplyVouchers(request);

        // Assert
        result.ShopResults.Should().ContainKey(sellerId);
        var shop = result.ShopResults[sellerId];

        // ItemsAmount = 1,000,000
        // ShopDiscount = 50,000 => Remaining base = 950,000
        // SystemDiscount = 10% of 950,000 = 95,000
        shop.ItemsAmount.Should().Be(1000000m);
        shop.ShopDiscountAmount.Should().Be(50000m);
        shop.SystemDiscountAmount.Should().Be(95000m);
        shop.ShippingFeeAmount.Should().Be(30000m);

        // Shopee Model:
        // NetSeller = ItemsAmount - ProductDiscount - ShopDiscount = 1,000,000 - 50,000 = 950,000
        // PlatformFee (5% of NetSeller) = 47,500 (deducted from Seller)
        // SellerReceiveAmount = 950,000 - 47,500 = 902,500
        // TotalAmount (Buyer pays) = NetSeller - SystemDiscount + ShippingFee = 950,000 - 95,000 + 30,000 = 885,000
        shop.PlatformFeeAmount.Should().Be(47500m);
        shop.SellerReceiveAmount.Should().Be(902500m);
        shop.TotalAmount.Should().Be(885000m);

        // Invariant check: TotalAmount == SellerReceive + (PlatformFee - SystemDiscount) + ShippingFee
        (shop.SellerReceiveAmount + (shop.PlatformFeeAmount - shop.SystemDiscountAmount) + shop.ShippingFeeAmount)
            .Should().Be(shop.TotalAmount);

        result.AppliedVoucherCodes.Should().Contain(new[] { "SHOP50K", "SYS10PCT" });
    }

    [Fact]
    public void Case2_ProRata3Shops_PennyRoundingAssignedToLastShop()
    {
        // Arrange
        var seller1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var seller2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var seller3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var items = new List<CalculationItemInput>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), seller1, "Item 1", 1, 100000m),
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), seller2, "Item 2", 1, 100000m),
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), seller3, "Item 3", 1, 100000m)
        };

        // System voucher for 100,000 VND off 300,000 VND order (100,000 / 3 = 33,333.333...)
        var systemVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SYS100K",
            Name = "System 100K Off",
            ScopeType = DiscountScopeType.System,
            OwnerSellerId = null,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 100000m,
            MinOrderAmount = 200000m,
            Status = DiscountStatus.Active
        };

        var request = new EvaluateVouchersRequest(
            Items: items,
            AvailableVouchers: new List<DiscountCode> { systemVoucher }
        );

        // Act
        var result = _engine.EvaluateAndApplyVouchers(request);

        // Assert
        var d1 = result.ShopResults[seller1].SystemDiscountAmount;
        var d2 = result.ShopResults[seller2].SystemDiscountAmount;
        var d3 = result.ShopResults[seller3].SystemDiscountAmount;

        // 100,000 * (100k / 300k) = 33,333.33 => 33,333
        d1.Should().Be(33333m);
        d2.Should().Be(33333m);
        // Last shop gets 100,000 - 33,333 - 33,333 = 33,334 (penny lock!)
        d3.Should().Be(33334m);

        (d1 + d2 + d3).Should().Be(100000m);
    }

    [Fact]
    public void Case3_PercentageVoucher_CappedAtMaxDiscountAmount()
    {
        // Arrange
        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "PCT50",
            Name = "50% off max 50k",
            ScopeType = DiscountScopeType.Shop,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 50m,
            MaxDiscountAmount = 50000m,
            Status = DiscountStatus.Active
        };

        // Act
        var discountOnLargeOrder = _engine.CalculateRawDiscount(1000000m, voucher);

        // Assert: 50% of 1,000,000 is 500,000 but capped at 50,000
        discountOnLargeOrder.Should().Be(50000m);
    }

    [Fact]
    public void Case4_SellerReceiveAmount_UnchangedWhenSystemVoucherIsApplied()
    {
        // Arrange: Compare seller receive amount with and without system voucher
        var sellerId = Guid.NewGuid();
        var item = new CalculationItemInput(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sellerId, "Product", 1, 500000m
        );

        var systemVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SYS50K",
            Name = "System 50K",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 50000m,
            Status = DiscountStatus.Active
        };

        var reqWithoutSys = new EvaluateVouchersRequest(
            Items: new List<CalculationItemInput> { item },
            AvailableVouchers: new List<DiscountCode>(),
            PlatformMarkupPercent: 5.0m
        );

        var reqWithSys = new EvaluateVouchersRequest(
            Items: new List<CalculationItemInput> { item },
            AvailableVouchers: new List<DiscountCode> { systemVoucher },
            PlatformMarkupPercent: 5.0m
        );

        // Act
        var resWithoutSys = _engine.EvaluateAndApplyVouchers(reqWithoutSys);
        var resWithSys = _engine.EvaluateAndApplyVouchers(reqWithSys);

        // Assert: Seller receives NetSeller - 5% fee in both cases!
        // System discount is borne by the platform.
        resWithoutSys.ShopResults[sellerId].SellerReceiveAmount
            .Should().Be(resWithSys.ShopResults[sellerId].SellerReceiveAmount);
    }

    [Fact]
    public void Case5_BlockVoucherStackingConflict_WhenMultipleSystemVouchersPassed()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var item = new CalculationItemInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sellerId, "Item", 1, 200000m);

        var sys1 = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SYS1",
            Name = "Sys 1",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            Status = DiscountStatus.Active
        };

        var sys2 = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SYS2",
            Name = "Sys 2",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 20000m,
            Status = DiscountStatus.Active
        };

        var request = new EvaluateVouchersRequest(
            Items: new List<CalculationItemInput> { item },
            AvailableVouchers: new List<DiscountCode> { sys1, sys2 }
        );

        // Act & Assert
        var act = () => _engine.EvaluateAndApplyVouchers(request);
        act.Should().Throw<CommerceDomainException>()
            .WithMessage("*Multiple system vouchers*")
            .Where(e => e.ErrorCode == "VOUCHER_STACKING_CONFLICT");
    }

    [Fact]
    public void Case6_UsedCountReachedMax_VoucherIsRejectedWithWarning()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var item = new CalculationItemInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sellerId, "Item", 1, 200000m);

        var voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "EXHAUSTED",
            Name = "Exhausted Voucher",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            MaxUsesTotal = 5,
            UsedCount = 4,
            ReservedCount = 1, // 4 + 1 = 5, reached limit!
            Status = DiscountStatus.Active
        };

        var request = new EvaluateVouchersRequest(
            Items: new List<CalculationItemInput> { item },
            AvailableVouchers: new List<DiscountCode> { voucher }
        );

        // Act
        var result = _engine.EvaluateAndApplyVouchers(request);

        // Assert
        result.RejectedVoucherCodes.Should().Contain("EXHAUSTED");
        result.Warnings.Should().ContainMatch("*total usage limit*");
    }

    [Fact]
    public void Case7_ProductVoucherForDifferentSeller_IsRejectedWithWarning()
    {
        // Arrange
        var sellerA = Guid.NewGuid();
        var sellerB = Guid.NewGuid();

        var itemFromSellerA = new CalculationItemInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sellerA, "Item A", 1, 200000m);

        var voucherForSellerB = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SHOP_B_PROD",
            Name = "Product Voucher B",
            ScopeType = DiscountScopeType.Product,
            OwnerSellerId = sellerB, // Belongs to seller B!
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 15000m,
            Status = DiscountStatus.Active
        };

        var request = new EvaluateVouchersRequest(
            Items: new List<CalculationItemInput> { itemFromSellerA },
            AvailableVouchers: new List<DiscountCode> { voucherForSellerB }
        );

        // Act
        var result = _engine.EvaluateAndApplyVouchers(request);

        // Assert
        result.RejectedVoucherCodes.Should().Contain("SHOP_B_PROD");
        result.Warnings.Should().ContainMatch("*not applicable*");
    }

    [Fact]
    public void Case8_BoundaryCheckOnEndsAt_ValidBeforeExpiryAndRejectedAfterExpiry()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var item = new CalculationItemInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sellerId, "Item", 1, 200000m);

        var validVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "VALID_VOUCHER",
            Name = "Valid Voucher",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            EndsAt = DateTimeOffset.UtcNow.AddMinutes(10), // Not expired
            Status = DiscountStatus.Active
        };

        var expiredVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "EXPIRED_VOUCHER",
            Name = "Expired Voucher",
            ScopeType = DiscountScopeType.System,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            EndsAt = DateTimeOffset.UtcNow.AddMinutes(-1), // Expired
            Status = DiscountStatus.Active
        };

        var request = new EvaluateVouchersRequest(
            Items: new List<CalculationItemInput> { item },
            AvailableVouchers: new List<DiscountCode> { validVoucher, expiredVoucher }
        );

        // Act
        var result = _engine.EvaluateAndApplyVouchers(request);

        // Assert
        result.AppliedVoucherCodes.Should().Contain("VALID_VOUCHER");
        result.RejectedVoucherCodes.Should().Contain("EXPIRED_VOUCHER");
        result.Warnings.Should().ContainMatch("*expired*");
    }

    [Fact]
    public void Case9_ShopLevelExclusivity_ShouldThrowShopMultipleVouchersNotAllowed_WhenMultipleVouchersPassedForSameShop()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var item = new CalculationItemInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sellerId, "Item", 2, 200000m);

        var prodVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "PROD10K",
            Name = "Prod 10K",
            ScopeType = DiscountScopeType.Product,
            OwnerSellerId = sellerId,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            Status = DiscountStatus.Active
        };

        var shopVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SHOP20K",
            Name = "Shop 20K",
            ScopeType = DiscountScopeType.Shop,
            OwnerSellerId = sellerId,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 20000m,
            Status = DiscountStatus.Active
        };

        var request = new EvaluateVouchersRequest(
            Items: new List<CalculationItemInput> { item },
            AvailableVouchers: new List<DiscountCode> { prodVoucher, shopVoucher }
        );

        // Act & Assert
        var act = () => _engine.EvaluateAndApplyVouchers(request);
        act.Should().Throw<CommerceDomainException>()
            .WithMessage("Only one voucher per shop can be applied.")
            .Where(e => e.ErrorCode == "SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED" && e.StatusCode == 400);
    }

    [Fact]
    public void Case10_MultiShopTwoTier_ShouldEnforceOneVoucherPerShopAndPlatformVoucher_WithShopeeFinancials()
    {
        // Arrange: Shop 1 (400k) with Shop voucher (20k off), Shop 2 (300k) with Product voucher (10k off), System voucher (30k off)
        var seller1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var seller2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var sku2 = Guid.NewGuid();

        var item1 = new CalculationItemInput(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), seller1, "Item Shop 1", 2, 200000m);
        var item2 = new CalculationItemInput(Guid.NewGuid(), Guid.NewGuid(), sku2, seller2, "Item Shop 2", 1, 300000m);

        var shop1Voucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SHOP1_20K",
            Name = "Shop 1 20K",
            ScopeType = DiscountScopeType.Shop,
            OwnerSellerId = seller1,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 20000m,
            MinOrderAmount = 100000m,
            Status = DiscountStatus.Active
        };

        var shop2ProdVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SHOP2_PROD10K",
            Name = "Shop 2 Prod 10K",
            ScopeType = DiscountScopeType.Product,
            OwnerSellerId = seller2,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 10000m,
            MinOrderAmount = 100000m,
            Status = DiscountStatus.Active,
            Scopes = new List<DiscountCodeScope>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    DiscountCodeId = Guid.NewGuid(),
                    ScopeRefType = DiscountScopeRefType.Sku,
                    ScopeRefId = sku2
                }
            }
        };

        var sysVoucher = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SYS30K",
            Name = "Sys 30K",
            ScopeType = DiscountScopeType.System,
            OwnerSellerId = null,
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 30000m,
            MinOrderAmount = 150000m,
            Status = DiscountStatus.Active
        };

        var request = new EvaluateVouchersRequest(
            Items: new List<CalculationItemInput> { item1, item2 },
            AvailableVouchers: new List<DiscountCode> { shop1Voucher, shop2ProdVoucher, sysVoucher },
            PlatformMarkupPercent: 5.0m,
            SellerShippingFees: new Dictionary<Guid, decimal>
            {
                [seller1] = 25000m,
                [seller2] = 30000m
            }
        );

        // Act
        var result = _engine.EvaluateAndApplyVouchers(request);

        // Assert
        result.AppliedVoucherCodes.Should().BeEquivalentTo(new[] { "SHOP1_20K", "SHOP2_PROD10K", "SYS30K" });

        var shop1Res = result.ShopResults[seller1];
        var shop2Res = result.ShopResults[seller2];

        // Shop 1:
        // ItemsAmount = 400,000, ShopDiscount = 20,000 => Base = 380,000
        shop1Res.ItemsAmount.Should().Be(400000m);
        shop1Res.ShopDiscountAmount.Should().Be(20000m);
        shop1Res.ProductDiscountAmount.Should().Be(0m);

        // Shop 2:
        // ItemsAmount = 300,000, ProductDiscount = 10,000 => Base = 290,000
        shop2Res.ItemsAmount.Should().Be(300000m);
        shop2Res.ProductDiscountAmount.Should().Be(10000m);
        shop2Res.ShopDiscountAmount.Should().Be(0m);

        // System Discount total = 30,000 across both shops
        (shop1Res.SystemDiscountAmount + shop2Res.SystemDiscountAmount).Should().Be(30000m);

        // Invariant for both shops: TotalAmount == SellerReceive + (PlatformFee - SystemDiscount) + ShippingFee
        (shop1Res.SellerReceiveAmount + (shop1Res.PlatformFeeAmount - shop1Res.SystemDiscountAmount) + shop1Res.ShippingFeeAmount)
            .Should().Be(shop1Res.TotalAmount);
        (shop2Res.SellerReceiveAmount + (shop2Res.PlatformFeeAmount - shop2Res.SystemDiscountAmount) + shop2Res.ShippingFeeAmount)
            .Should().Be(shop2Res.TotalAmount);
    }
}
