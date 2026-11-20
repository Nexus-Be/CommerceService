namespace Commerce.UnitTests.Domain;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;

public class FinancialInvariantTests
{
    [Fact]
    public void CalculateFinancials_ShouldSatisfyTotalAmountInvariant_WhenPositivePlatformFee()
    {
        // Arrange
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "ORD-20261029-000001",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new(),
            ContactPhone = "0901234567",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            Items = new List<SubOrderItem>
            {
                new SubOrderItem
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    SkuId = Guid.NewGuid(),
                    SellerId = Guid.NewGuid(),
                    ProductNameSnapshot = "Item 1",
                    Quantity = 2,
                    UnitPrice = 500000m,
                    UnitPriceAfterDiscount = 450000m,
                    LineTotal = 900000m
                }
            }
        };

        // Act
        // Shopee / TikTok Shop Model:
        // ItemsAmount = 1.000.000, ProductDiscount = 100.000, ShopDiscount = 50.000
        // NetSeller = 850.000. Commission 5% = 42.500 (deducted from Seller sales).
        // SellerReceiveAmount = 850.000 - 42.500 = 807.500
        // SystemDiscount = 20.000, ShippingFee = 30.000
        // TotalAmount (Buyer pays) = 850.000 - 20.000 + 30.000 = 860.000
        subOrder.CalculateFinancials(
            shippingFee: 30000m,
            shopDiscount: 50000m,
            systemDiscount: 20000m,
            platformMarkupPercent: 5m);

        // Assert
        subOrder.ItemsAmount.Should().Be(1000000m);
        subOrder.ProductDiscountAmount.Should().Be(100000m);
        subOrder.ShopDiscountAmount.Should().Be(50000m);
        subOrder.PlatformFeeAmount.Should().Be(42500m);
        subOrder.SellerReceiveAmount.Should().Be(807500m);
        subOrder.ShippingFeeAmount.Should().Be(30000m);
        subOrder.TotalAmount.Should().Be(860000m);

        // Strict Shopee Accounting Invariant Check:
        // TotalAmount == SellerReceiveAmount + (PlatformFeeAmount - SystemDiscountAmount) + ShippingFeeAmount
        (subOrder.SellerReceiveAmount + (subOrder.PlatformFeeAmount - subOrder.SystemDiscountAmount) + subOrder.ShippingFeeAmount)
            .Should().Be(subOrder.TotalAmount);
    }

    [Fact]
    public void CalculateFinancials_ShouldAllowNegativePlatformNetRetained_WhenSystemVoucherExceedsPlatformFee()
    {
        // Arrange
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "ORD-20261029-000002",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new(),
            ContactPhone = "0901234567",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            Items = new List<SubOrderItem>
            {
                new SubOrderItem
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    SkuId = Guid.NewGuid(),
                    SellerId = Guid.NewGuid(),
                    ProductNameSnapshot = "Item 2",
                    Quantity = 1,
                    UnitPrice = 1000000m,
                    UnitPriceAfterDiscount = 1000000m,
                    LineTotal = 1000000m
                }
            }
        };

        // Act
        // Seller Net = 1.000.000, Platform Fee 3% = 30.000 => Seller receives 970.000
        // Sàn tài trợ voucher 100.000 => Net retained = 30.000 - 100.000 = -70.000 (Sàn bù lỗ 70k)
        // Ship = 25.000 => Total = 1.000.000 - 100.000 + 25.000 = 925.000
        subOrder.CalculateFinancials(
            shippingFee: 25000m,
            shopDiscount: 0m,
            systemDiscount: 100000m,
            platformMarkupPercent: 3m);

        // Assert
        subOrder.SellerReceiveAmount.Should().Be(970000m);
        subOrder.PlatformFeeAmount.Should().Be(30000m);
        subOrder.TotalAmount.Should().Be(925000m);

        // Invariant MUST strictly hold true
        (subOrder.SellerReceiveAmount + (subOrder.PlatformFeeAmount - subOrder.SystemDiscountAmount) + subOrder.ShippingFeeAmount)
            .Should().Be(subOrder.TotalAmount);
    }

    [Fact]
    public void CalculateFinancials_ShouldMatchAcceptanceScenario1_ExactValues()
    {
        // Arrange
        // Items totaling 1,000,000 VND, shop discount 50,000 VND, commission 5%, system voucher 20,000 VND, shipping fee 30,000 VND
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "ORD-20261029-000003",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new(),
            ContactPhone = "0901234567",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            Items = new List<SubOrderItem>
            {
                new SubOrderItem
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    SkuId = Guid.NewGuid(),
                    SellerId = Guid.NewGuid(),
                    ProductNameSnapshot = "Item 3",
                    Quantity = 2,
                    UnitPrice = 500000m,
                    UnitPriceAfterDiscount = 500000m,
                    LineTotal = 1000000m
                }
            }
        };

        // Act
        subOrder.CalculateFinancials(
            shippingFee: 30000m,
            shopDiscount: 50000m,
            systemDiscount: 20000m,
            platformMarkupPercent: 5m);

        // Assert
        // NetSeller = 1,000,000 - 50,000 = 950,000
        // Commission 5% = 47,500 => Seller receives 902,500
        // TotalAmount (Buyer pays) = 950,000 - 20,000 + 30,000 = 960,000
        subOrder.ItemsAmount.Should().Be(1000000m);
        subOrder.ProductDiscountAmount.Should().Be(0m);
        subOrder.ShopDiscountAmount.Should().Be(50000m);
        subOrder.PlatformFeeAmount.Should().Be(47500m);
        subOrder.SellerReceiveAmount.Should().Be(902500m);
        subOrder.ShippingFeeAmount.Should().Be(30000m);
        subOrder.TotalAmount.Should().Be(960000m);
        (subOrder.SellerReceiveAmount + (subOrder.PlatformFeeAmount - subOrder.SystemDiscountAmount) + subOrder.ShippingFeeAmount)
            .Should().Be(subOrder.TotalAmount);
    }

    [Fact]
    public void CalculateFinancials_ShouldHandleZeroSellerReceiveAmount_WhenDiscountEqualsItems()
    {
        // Arrange
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "ORD-20261029-000004",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new(),
            ContactPhone = "0901234567",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            Items = new List<SubOrderItem>
            {
                new SubOrderItem
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    SkuId = Guid.NewGuid(),
                    SellerId = Guid.NewGuid(),
                    ProductNameSnapshot = "Item 4",
                    Quantity = 1,
                    UnitPrice = 200000m,
                    UnitPriceAfterDiscount = 200000m,
                    LineTotal = 200000m
                }
            }
        };

        // Act
        subOrder.CalculateFinancials(
            shippingFee: 20000m,
            shopDiscount: 200000m,
            systemDiscount: 0m,
            platformMarkupPercent: 5m);

        // Assert
        subOrder.SellerReceiveAmount.Should().Be(0m);
        subOrder.PlatformMarkupAmount.Should().Be(0m);
        subOrder.PlatformFeeAmount.Should().Be(0m);
        subOrder.TotalAmount.Should().Be(20000m); // Only shipping fee remains
        (subOrder.SellerReceiveAmount + (subOrder.PlatformFeeAmount - subOrder.SystemDiscountAmount) + subOrder.ShippingFeeAmount)
            .Should().Be(subOrder.TotalAmount);
    }
}
