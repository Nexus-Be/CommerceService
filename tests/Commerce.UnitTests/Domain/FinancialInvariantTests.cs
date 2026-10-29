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
            ShippingAddress = "{}",
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
        // ItemsAmount = 1.000.000, ProductDiscount = 100.000, ShopDiscount = 50.000
        // Net = 850.000, Markup 5% = 42.500, SystemDiscount = 20.000 => PlatformFee = 22.500
        // Ship = 30.000 => Total = 850.000 + 22.500 + 30.000 = 902.500
        subOrder.CalculateFinancials(
            shippingFee: 30000m,
            shopDiscount: 50000m,
            systemDiscount: 20000m,
            platformMarkupPercent: 5m);

        // Assert
        subOrder.ItemsAmount.Should().Be(1000000m);
        subOrder.ProductDiscountAmount.Should().Be(100000m);
        subOrder.ShopDiscountAmount.Should().Be(50000m);
        subOrder.SellerReceiveAmount.Should().Be(850000m);
        subOrder.PlatformMarkupAmount.Should().Be(42500m);
        subOrder.PlatformFeeAmount.Should().Be(22500m);
        subOrder.ShippingFeeAmount.Should().Be(30000m);
        subOrder.TotalAmount.Should().Be(902500m);

        // Strict DB Invariant Check
        (subOrder.SellerReceiveAmount + subOrder.PlatformFeeAmount + subOrder.ShippingFeeAmount)
            .Should().Be(subOrder.TotalAmount);
    }

    [Fact]
    public void CalculateFinancials_ShouldAllowNegativePlatformFee_WhenSystemVoucherExceedsPlatformMarkup()
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
            ShippingAddress = "{}",
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
        // Seller Net = 1.000.000, Platform Markup 3% = 30.000
        // Sàn tài trợ voucher 100.000 => PlatformFee = 30.000 - 100.000 = -70.000 (Âm 70k)
        // Ship = 25.000 => Total = 1.000.000 + (-70.000) + 25.000 = 955.000
        subOrder.CalculateFinancials(
            shippingFee: 25000m,
            shopDiscount: 0m,
            systemDiscount: 100000m,
            platformMarkupPercent: 3m);

        // Assert
        subOrder.SellerReceiveAmount.Should().Be(1000000m);
        subOrder.PlatformFeeAmount.Should().Be(-70000m);
        subOrder.TotalAmount.Should().Be(955000m);

        // Invariant MUST strictly hold true
        subOrder.TotalAmount.Should().Be(subOrder.SellerReceiveAmount + subOrder.PlatformFeeAmount + subOrder.ShippingFeeAmount);
    }
}
