namespace Commerce.Application.Services.Discount;

using System;
using System.Collections.Generic;
using System.Linq;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;

public sealed class VoucherCalculationEngine : IVoucherCalculationEngine
{
    public decimal CalculateRawDiscount(decimal baseAmount, DiscountCode voucher)
    {
        if (baseAmount <= 0m)
        {
            return 0m;
        }

        decimal discount = 0m;
        if (voucher.DiscountType == DiscountType.FixedAmount)
        {
            discount = Math.Min(voucher.DiscountValue, baseAmount);
        }
        else if (voucher.DiscountType == DiscountType.Percentage)
        {
            decimal raw = baseAmount * (voucher.DiscountValue / 100m);
            if (voucher.MaxDiscountAmount.HasValue && voucher.MaxDiscountAmount.Value > 0)
            {
                raw = Math.Min(raw, voucher.MaxDiscountAmount.Value);
            }
            discount = Math.Min(raw, baseAmount);
        }

        decimal rounded = Math.Round(discount, 0, MidpointRounding.AwayFromZero);
        return Math.Max(0m, rounded);
    }

    public EvaluateVouchersResult EvaluateAndApplyVouchers(EvaluateVouchersRequest request)
    {
        var items = request.Items ?? Array.Empty<CalculationItemInput>();
        var vouchers = request.AvailableVouchers ?? Array.Empty<DiscountCode>();
        var shippingFees = request.SellerShippingFees ?? new Dictionary<Guid, decimal>();
        var now = DateTimeOffset.UtcNow;

        var warnings = new List<string>();
        var rejectedVoucherCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rejectedReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var appliedVoucherCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allAllocatedUsages = new List<AllocatedVoucherUsage>();

        // 1. Group items by SellerId
        var shopItems = items.GroupBy(i => i.SellerId).ToDictionary(g => g.Key, g => g.ToList());

        // Stacking conflict detection across active candidate vouchers
        var candidateVouchers = new List<DiscountCode>();
        foreach (var v in vouchers)
        {
            if (v.Status != DiscountStatus.Active)
            {
                warnings.Add($"Voucher '{v.Code}' is not active.");
                rejectedVoucherCodes.Add(v.Code);
                rejectedReasons[v.Code] = "NOT_ACTIVE";
                continue;
            }

            if (v.StartsAt.HasValue && now < v.StartsAt.Value)
            {
                warnings.Add($"Voucher '{v.Code}' has not started yet.");
                rejectedVoucherCodes.Add(v.Code);
                rejectedReasons[v.Code] = "NOT_STARTED";
                continue;
            }

            if (v.EndsAt.HasValue && now >= v.EndsAt.Value)
            {
                warnings.Add($"Voucher '{v.Code}' has expired.");
                rejectedVoucherCodes.Add(v.Code);
                rejectedReasons[v.Code] = "EXPIRED";
                continue;
            }

            if (v.MaxUsesTotal.HasValue && v.UsedCount + v.ReservedCount >= v.MaxUsesTotal.Value)
            {
                warnings.Add($"Voucher '{v.Code}' has reached its total usage limit.");
                rejectedVoucherCodes.Add(v.Code);
                rejectedReasons[v.Code] = "USAGE_LIMIT_REACHED";
                continue;
            }

            candidateVouchers.Add(v);
        }

        // Stacking Conflict Matrix enforcement
        var systemCandidates = candidateVouchers.Where(v => v.ScopeType == DiscountScopeType.System).ToList();
        if (systemCandidates.Count > 1)
        {
            throw new CommerceDomainException("VOUCHER_STACKING_CONFLICT", "Multiple system vouchers cannot be applied to the same order.", 400) { ReasonCode = "STACKING_CONFLICT" };
        }

        // Shop-Level Exclusivity Guard (Shopee 2-Tier Standard):
        // Each shop can have at most ONE voucher (Product OR Shop scope).
        // Multiple vouchers for the same shop are strictly forbidden to prevent double-discounting margin erosion.
        var shopLevelVouchers = candidateVouchers
            .Where(v => v.ScopeType == DiscountScopeType.Shop || v.ScopeType == DiscountScopeType.Product)
            .ToList();

        var multipleShopVoucher = shopLevelVouchers
            .GroupBy(v => v.OwnerSellerId)
            .FirstOrDefault(g => g.Key.HasValue && g.Count() > 1);

        if (multipleShopVoucher != null)
        {
            throw new CommerceDomainException("SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED", "Only one voucher per shop can be applied.", 400) { ReasonCode = "STACKING_CONFLICT" };
        }

        var shopCandidates = candidateVouchers.Where(v => v.ScopeType == DiscountScopeType.Shop).ToList();
        var productCandidates = candidateVouchers.Where(v => v.ScopeType == DiscountScopeType.Product).ToList();

        // Structure to track amounts per shop
        var shopDiscounts = shopItems.Keys.ToDictionary(
            sellerId => sellerId,
            sellerId => new
            {
                SellerId = sellerId,
                Items = shopItems[sellerId],
                ItemsAmount = shopItems[sellerId].Sum(i => i.Quantity * i.UnitPrice),
                ProductDiscounts = new Dictionary<Guid, decimal>(),
                ShopDiscount = 0m,
                SystemDiscount = 0m,
                AllocatedUsages = new List<AllocatedVoucherUsage>()
            }
        );

        // Mutable tracking
        var productDiscountPerShop = shopItems.Keys.ToDictionary(k => k, _ => 0m);
        var shopDiscountPerShop = shopItems.Keys.ToDictionary(k => k, _ => 0m);
        var systemDiscountPerShop = shopItems.Keys.ToDictionary(k => k, _ => 0m);
        var usagesPerShop = shopItems.Keys.ToDictionary(k => k, _ => new List<AllocatedVoucherUsage>());
        var itemProductDiscountsPerShop = shopItems.Keys.ToDictionary(k => k, _ => new Dictionary<Guid, decimal>());

        // 2. Tier 1: Product / SKU vouchers
        foreach (var pv in productCandidates)
        {
            if (!pv.OwnerSellerId.HasValue || !shopItems.TryGetValue(pv.OwnerSellerId.Value, out var sellerItems))
            {
                warnings.Add($"Product voucher '{pv.Code}' is not applicable to any items in cart.");
                rejectedVoucherCodes.Add(pv.Code);
                rejectedReasons[pv.Code] = "SCOPE_MISMATCH";
                continue;
            }

            var sellerId = pv.OwnerSellerId.Value;
            var activeScopes = (pv.Scopes ?? Enumerable.Empty<DiscountCodeScope>())
                .Where(s => s.ScopeRefId.HasValue)
                .ToList();

            var targetSkuIds = activeScopes
                .Where(s => s.ScopeRefType == DiscountScopeRefType.Sku)
                .Select(s => s.ScopeRefId!.Value)
                .ToHashSet();

            var targetProductIds = activeScopes
                .Where(s => s.ScopeRefType == DiscountScopeRefType.Product)
                .Select(s => s.ScopeRefId!.Value)
                .ToHashSet();

            var hasProductOrSkuScopes = targetSkuIds.Count > 0 || targetProductIds.Count > 0;
            var eligibleItems = hasProductOrSkuScopes
                ? sellerItems.Where(i => targetSkuIds.Contains(i.SkuId) || targetProductIds.Contains(i.ProductId)).ToList()
                : (activeScopes.Count == 0 ? sellerItems : new List<CalculationItemInput>());

            if (eligibleItems.Count == 0)
            {
                warnings.Add($"Product voucher '{pv.Code}' is not applicable to any items in cart.");
                rejectedVoucherCodes.Add(pv.Code);
                rejectedReasons[pv.Code] = "SCOPE_MISMATCH";
                continue;
            }

            var baseAmount = eligibleItems.Sum(i => i.Quantity * i.UnitPrice);
            if (baseAmount < pv.MinOrderAmount)
            {
                warnings.Add($"Minimum order amount {pv.MinOrderAmount:N0} VND not met for voucher '{pv.Code}'.");
                rejectedVoucherCodes.Add(pv.Code);
                rejectedReasons[pv.Code] = "MIN_ORDER_NOT_MET";
                continue;
            }

            var discount = CalculateRawDiscount(baseAmount, pv);
            if (discount > 0m)
            {
                productDiscountPerShop[sellerId] += discount;
                var usage = new AllocatedVoucherUsage(pv.Id, pv.Code, DiscountScopeType.Product, sellerId, discount);
                usagesPerShop[sellerId].Add(usage);
                allAllocatedUsages.Add(usage);
                appliedVoucherCodes.Add(pv.Code);

                // Allocate product discount across eligible SKUs for item-level unit price breakdown
                var sortedEligible = eligibleItems.OrderBy(i => i.SkuId).ToList();
                decimal allocatedItemSum = 0m;
                for (int idx = 0; idx < sortedEligible.Count; idx++)
                {
                    var elItem = sortedEligible[idx];
                    var elLineTotal = elItem.Quantity * elItem.UnitPrice;
                    decimal itemDiscount = idx == sortedEligible.Count - 1
                        ? discount - allocatedItemSum
                        : Math.Round(discount * (elLineTotal / baseAmount), 0, MidpointRounding.AwayFromZero);
                    itemDiscount = Math.Clamp(itemDiscount, 0m, elLineTotal);
                    if (idx < sortedEligible.Count - 1)
                    {
                        allocatedItemSum += itemDiscount;
                    }
                    if (itemDiscount > 0m && elItem.SkuId != Guid.Empty)
                    {
                        itemProductDiscountsPerShop[sellerId][elItem.SkuId] =
                            itemProductDiscountsPerShop[sellerId].GetValueOrDefault(elItem.SkuId) + itemDiscount;
                    }
                }
            }
        }

        // 3. Tier 2: Shop vouchers
        foreach (var sv in shopCandidates)
        {
            if (!sv.OwnerSellerId.HasValue || !shopItems.TryGetValue(sv.OwnerSellerId.Value, out var sellerItems))
            {
                warnings.Add($"Shop voucher '{sv.Code}' does not match any shop in the cart.");
                rejectedVoucherCodes.Add(sv.Code);
                rejectedReasons[sv.Code] = "SCOPE_MISMATCH";
                continue;
            }

            var sellerId = sv.OwnerSellerId.Value;
            var initialItemsAmount = sellerItems.Sum(i => i.Quantity * i.UnitPrice);
            var remainingShopBase = Math.Max(0m, initialItemsAmount - productDiscountPerShop[sellerId]);

            if (remainingShopBase < sv.MinOrderAmount)
            {
                warnings.Add($"Minimum order amount {sv.MinOrderAmount:N0} VND not met for shop voucher '{sv.Code}'.");
                rejectedVoucherCodes.Add(sv.Code);
                rejectedReasons[sv.Code] = "MIN_ORDER_NOT_MET";
                continue;
            }

            var discount = CalculateRawDiscount(remainingShopBase, sv);
            if (discount > 0m)
            {
                shopDiscountPerShop[sellerId] += discount;
                var usage = new AllocatedVoucherUsage(sv.Id, sv.Code, DiscountScopeType.Shop, sellerId, discount);
                usagesPerShop[sellerId].Add(usage);
                allAllocatedUsages.Add(usage);
                appliedVoucherCodes.Add(sv.Code);
            }
        }

        // 4. Tier 3: System vouchers (Platform-wide)
        if (systemCandidates.Count == 1)
        {
            var sysVoucher = systemCandidates[0];

            // Base for system voucher is remaining items amount after Tier 1 and Tier 2 deductions
            var eligibleShops = shopItems.Keys
                .Select(sellerId =>
                {
                    var itemsAmount = shopItems[sellerId].Sum(i => i.Quantity * i.UnitPrice);
                    var netBase = Math.Max(0m, itemsAmount - productDiscountPerShop[sellerId] - shopDiscountPerShop[sellerId]);
                    return new { SellerId = sellerId, Base = netBase };
                })
                .Where(s => s.Base > 0m)
                .OrderBy(s => s.SellerId)
                .ToList();

            var totalSystemBase = eligibleShops.Sum(s => s.Base);

            if (totalSystemBase < sysVoucher.MinOrderAmount)
            {
                warnings.Add($"Minimum order amount {sysVoucher.MinOrderAmount:N0} VND not met for system voucher '{sysVoucher.Code}'.");
                rejectedVoucherCodes.Add(sysVoucher.Code);
                rejectedReasons[sysVoucher.Code] = "MIN_ORDER_NOT_MET";
            }
            else
            {
                var totalSystemDiscount = CalculateRawDiscount(totalSystemBase, sysVoucher);
                if (totalSystemDiscount > 0m && eligibleShops.Count > 0)
                {
                    // Penny rounding lock: Pro-rata allocation with deterministic ascending SellerId order
                    decimal allocatedSum = 0m;
                    int count = eligibleShops.Count;

                    for (int j = 0; j < count; j++)
                    {
                        var shop = eligibleShops[j];
                        decimal shopDiscount;

                        if (j == count - 1)
                        {
                            // Last shop receives remainder to lock penny drift
                            shopDiscount = totalSystemDiscount - allocatedSum;
                        }
                        else
                        {
                            shopDiscount = Math.Round(totalSystemDiscount * (shop.Base / totalSystemBase), 0, MidpointRounding.AwayFromZero);
                            allocatedSum += shopDiscount;
                        }

                        // Ensure shop discount doesn't exceed shop base
                        shopDiscount = Math.Min(shopDiscount, shop.Base);

                        if (shopDiscount > 0m)
                        {
                            systemDiscountPerShop[shop.SellerId] += shopDiscount;
                            var usage = new AllocatedVoucherUsage(sysVoucher.Id, sysVoucher.Code, DiscountScopeType.System, shop.SellerId, shopDiscount);
                            usagesPerShop[shop.SellerId].Add(usage);
                            allAllocatedUsages.Add(usage);
                        }
                    }

                    appliedVoucherCodes.Add(sysVoucher.Code);
                }
            }
        }

        // 5. Final Step: Build ShopCalculationResult under Shopee / TikTok Shop model
        var shopResults = new Dictionary<Guid, ShopCalculationResult>();
        foreach (var sellerId in shopItems.Keys)
        {
            var sellerItems = shopItems[sellerId];
            var itemsAmount = sellerItems.Sum(i => i.Quantity * i.UnitPrice);
            var productDiscount = productDiscountPerShop[sellerId];
            var shopDiscount = shopDiscountPerShop[sellerId];
            var systemDiscount = systemDiscountPerShop[sellerId];
            var shippingFee = shippingFees.TryGetValue(sellerId, out var fee) ? fee : 0m;

            // Shopee / TikTok Shop model:
            // NetSeller = ItemsAmount - ProductDiscount - ShopDiscount
            var netSeller = Math.Max(0m, itemsAmount - productDiscount - shopDiscount);

            // PlatformFeeAmount = Math.Round(NetSeller * (markupPercent / 100m), 0, AwayFromZero) (Deducted from Seller)
            var platformFee = Math.Round(netSeller * (request.PlatformMarkupPercent / 100m), 0, MidpointRounding.AwayFromZero);

            // SellerReceiveAmount = NetSeller - PlatformFeeAmount (Seller payout after commission)
            var sellerReceive = Math.Max(0m, netSeller - platformFee);

            // Buyer pays catalog price minus all discounts + shipping (TotalAmount = NetSeller - SystemDiscount + ShippingFee)
            var totalAmount = Math.Max(0m, netSeller - systemDiscount) + shippingFee;

            var result = new ShopCalculationResult(
                SellerId: sellerId,
                ItemsAmount: itemsAmount,
                ProductDiscountAmount: productDiscount,
                ShopDiscountAmount: shopDiscount,
                SystemDiscountAmount: systemDiscount,
                ShippingFeeAmount: shippingFee,
                PlatformFeeAmount: platformFee,
                SellerReceiveAmount: sellerReceive,
                TotalAmount: totalAmount,
                AllocatedUsages: usagesPerShop[sellerId],
                ItemProductDiscounts: itemProductDiscountsPerShop[sellerId].Count > 0 ? itemProductDiscountsPerShop[sellerId] : null
            );

            shopResults[sellerId] = result;
        }

        return new EvaluateVouchersResult(
            ShopResults: shopResults,
            AllAllocatedUsages: allAllocatedUsages,
            AppliedVoucherCodes: appliedVoucherCodes.ToList(),
            RejectedVoucherCodes: rejectedVoucherCodes.ToList(),
            Warnings: warnings,
            RejectedReasons: rejectedReasons
        );
    }
}
