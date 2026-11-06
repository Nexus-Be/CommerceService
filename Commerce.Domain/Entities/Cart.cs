using System;
using System.Collections.Generic;
using System.Linq;
using Commerce.Domain.Base;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;

namespace Commerce.Domain.Entities;

public class Cart : BaseEntity<Guid>
{
    public required Guid BuyerId { get; set; }
    public required CartStatus Status { get; set; } = CartStatus.Active;
    public int ItemCount { get; set; }
    public decimal SubtotalAmount { get; set; }
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public long RowVersion { get; set; }

    // Navigation
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();

    public CartItem AddOrUpdateItem(Guid productId, Guid skuId, Guid sellerId, string productName, int quantity, decimal unitPrice)
    {
        if (quantity <= 0)
        {
            throw new CommerceDomainException("VAL_INVALID_INPUT", "Quantity must be greater than zero.");
        }

        var existingItem = Items.FirstOrDefault(i => i.SkuId == skuId);
        if (existingItem != null)
        {
            var newQuantity = existingItem.Quantity + quantity;
            if (newQuantity > 99)
            {
                throw new CommerceDomainException("CART_ITEM_LIMIT_EXCEEDED", "Total quantity for a single product cannot exceed 99.");
            }

            existingItem.Quantity = newQuantity;
            existingItem.UnitPriceSnapshot = unitPrice;
            existingItem.ProductNameSnapshot = productName;
            RecalculateTotals();
            return existingItem;
        }

        if (Items.Count >= 50)
        {
            throw new CommerceDomainException("CART_LIMIT_EXCEEDED", "Cart cannot exceed maximum limit of 50 different items.");
        }

        if (quantity > 99)
        {
            throw new CommerceDomainException("CART_ITEM_LIMIT_EXCEEDED", "Quantity for a single product cannot exceed 99.");
        }

        var newItem = new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = Id,
            ProductId = productId,
            SkuId = skuId,
            SellerId = sellerId,
            ProductNameSnapshot = productName,
            Quantity = quantity,
            UnitPriceSnapshot = unitPrice,
            AddedAt = DateTimeOffset.UtcNow
        };

        Items.Add(newItem);
        RecalculateTotals();
        return newItem;
    }

    public CartItem UpdateItemQuantity(Guid cartItemId, int newQuantity)
    {
        if (newQuantity <= 0)
        {
            throw new CommerceDomainException("VAL_INVALID_INPUT", "Quantity must be greater than zero.");
        }

        if (newQuantity > 99)
        {
            throw new CommerceDomainException("CART_ITEM_LIMIT_EXCEEDED", "Total quantity for a single product cannot exceed 99.");
        }

        var item = Items.FirstOrDefault(i => i.Id == cartItemId)
            ?? throw new CommerceDomainException("CART_ITEM_NOT_FOUND", "Cart item not found in the current cart.", 404);

        item.Quantity = newQuantity;
        RecalculateTotals();
        return item;
    }

    public void RemoveItem(Guid cartItemId)
    {
        var item = Items.FirstOrDefault(i => i.Id == cartItemId)
            ?? throw new CommerceDomainException("CART_ITEM_NOT_FOUND", "Cart item not found in the current cart.", 404);

        Items.Remove(item);
        RecalculateTotals();
    }

    public void ClearItems()
    {
        Items.Clear();
        RecalculateTotals();
    }

    public void BulkRemoveItems(IEnumerable<Guid> cartItemIds)
    {
        var targetIds = cartItemIds.ToHashSet();
        var itemsToRemove = Items.Where(i => targetIds.Contains(i.Id)).ToList();
        foreach (var item in itemsToRemove)
        {
            Items.Remove(item);
        }
        RecalculateTotals();
    }

    public CartItem ChangeItemVariant(Guid cartItemId, Guid newSkuId, string newProductName, decimal newUnitPrice)
    {
        var currentItem = Items.FirstOrDefault(i => i.Id == cartItemId)
            ?? throw new CommerceDomainException("CART_ITEM_NOT_FOUND", "Cart item not found in the current cart.", 404);

        if (currentItem.SkuId == newSkuId)
        {
            return currentItem;
        }

        var existingDuplicate = Items.FirstOrDefault(i => i.SkuId == newSkuId && i.Id != cartItemId);
        if (existingDuplicate != null)
        {
            var mergedQuantity = existingDuplicate.Quantity + currentItem.Quantity;
            if (mergedQuantity > 99) mergedQuantity = 99;

            existingDuplicate.Quantity = mergedQuantity;
            existingDuplicate.UnitPriceSnapshot = newUnitPrice;
            existingDuplicate.ProductNameSnapshot = newProductName;
            Items.Remove(currentItem);
            RecalculateTotals();
            return existingDuplicate;
        }

        currentItem.SkuId = newSkuId;
        currentItem.ProductNameSnapshot = newProductName;
        currentItem.UnitPriceSnapshot = newUnitPrice;
        RecalculateTotals();
        return currentItem;
    }

    public void RecalculateTotals()
    {
        ItemCount = Items.Count;
        SubtotalAmount = Items.Sum(i => i.Quantity * i.UnitPriceSnapshot);
        LastActivityAt = DateTimeOffset.UtcNow;
        RowVersion++;
    }
}
