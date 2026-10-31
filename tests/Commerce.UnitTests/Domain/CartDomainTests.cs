using System;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace Commerce.UnitTests.Domain;

public class CartDomainTests
{
    private Cart CreateEmptyCart()
    {
        return new Cart
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            Status = CartStatus.Active,
            ItemCount = 0,
            SubtotalAmount = 0m,
            RowVersion = 1
        };
    }

    [Fact]
    public void AddOrUpdateItem_ShouldAddNewItem_WhenSkuNotInCart()
    {
        var cart = CreateEmptyCart();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var item = cart.AddOrUpdateItem(productId, skuId, sellerId, "Test Product", 2, 50000m);

        item.Should().NotBeNull();
        item.Quantity.Should().Be(2);
        item.UnitPriceSnapshot.Should().Be(50000m);
        cart.ItemCount.Should().Be(1);
        cart.SubtotalAmount.Should().Be(100000m);
    }

    [Fact]
    public void AddOrUpdateItem_ShouldAccumulateQuantity_WhenSkuAlreadyInCart()
    {
        var cart = CreateEmptyCart();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        cart.AddOrUpdateItem(productId, skuId, sellerId, "Test Product", 2, 50000m);
        cart.AddOrUpdateItem(productId, skuId, sellerId, "Test Product", 3, 50000m);

        cart.ItemCount.Should().Be(1);
        cart.Items.First().Quantity.Should().Be(5);
        cart.SubtotalAmount.Should().Be(250000m);
    }

    [Fact]
    public void AddOrUpdateItem_ShouldThrow_WhenQuantityExceeds99()
    {
        var cart = CreateEmptyCart();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var act = () => cart.AddOrUpdateItem(productId, skuId, sellerId, "Test Product", 100, 50000m);

        act.Should().Throw<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CART_ITEM_LIMIT_EXCEEDED");
    }

    [Fact]
    public void AddOrUpdateItem_ShouldThrow_WhenTotalAccumulatedQuantityExceeds99()
    {
        var cart = CreateEmptyCart();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        cart.AddOrUpdateItem(productId, skuId, sellerId, "Test Product", 90, 50000m);
        var act = () => cart.AddOrUpdateItem(productId, skuId, sellerId, "Test Product", 10, 50000m);

        act.Should().Throw<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CART_ITEM_LIMIT_EXCEEDED");
    }

    [Fact]
    public void AddOrUpdateItem_ShouldThrow_WhenCartItemCountExceeds50()
    {
        var cart = CreateEmptyCart();
        for (int i = 0; i < 50; i++)
        {
            cart.AddOrUpdateItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), $"Product {i}", 1, 1000m);
        }

        cart.ItemCount.Should().Be(50);
        var act = () => cart.AddOrUpdateItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Product 51", 1, 1000m);

        act.Should().Throw<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CART_LIMIT_EXCEEDED");
    }

    [Fact]
    public void AddOrUpdateItem_ShouldThrow_WhenQuantityIsZeroOrNegative()
    {
        var cart = CreateEmptyCart();

        var actZero = () => cart.AddOrUpdateItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Product", 0, 1000m);
        var actNeg = () => cart.AddOrUpdateItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Product", -1, 1000m);

        actZero.Should().Throw<CommerceDomainException>().Where(e => e.ErrorCode == "VAL_INVALID_INPUT");
        actNeg.Should().Throw<CommerceDomainException>().Where(e => e.ErrorCode == "VAL_INVALID_INPUT");
    }

    [Fact]
    public void UpdateItemQuantity_ShouldUpdateAndRecalculateTotals()
    {
        var cart = CreateEmptyCart();
        var item = cart.AddOrUpdateItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Product", 2, 50000m);

        cart.UpdateItemQuantity(item.Id, 4);

        item.Quantity.Should().Be(4);
        cart.SubtotalAmount.Should().Be(200000m);
    }

    [Fact]
    public void RemoveItem_ShouldRemoveAndRecalculateTotals()
    {
        var cart = CreateEmptyCart();
        var item1 = cart.AddOrUpdateItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Product 1", 2, 50000m);
        var item2 = cart.AddOrUpdateItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Product 2", 1, 30000m);

        cart.RemoveItem(item1.Id);

        cart.ItemCount.Should().Be(1);
        cart.SubtotalAmount.Should().Be(30000m);
        cart.Items.Should().NotContain(item1);
    }

    [Fact]
    public void ClearItems_ShouldEmptyCartAndResetTotals()
    {
        var cart = CreateEmptyCart();
        cart.AddOrUpdateItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Product 1", 2, 50000m);
        cart.AddOrUpdateItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Product 2", 1, 30000m);

        cart.ClearItems();

        cart.ItemCount.Should().Be(0);
        cart.SubtotalAmount.Should().Be(0m);
        cart.Items.Should().BeEmpty();
    }
}
