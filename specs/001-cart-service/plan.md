# Implementation Plan: Cart Service Phase 3 (Advanced Cart & Checkout Bridge)

**Feature**: `001-cart-service` | **Branch**: `feat/cart-service-v2` | **Date**: 2026-11-01  
**Spec**: [specs/001-cart-service/spec.md](file:///d:/CommerceService/specs/001-cart-service/spec.md)  
**SRS Reference**: [docs/SRS_Commerce_Service_v1.md](file:///d:/CommerceService/docs/SRS_Commerce_Service_v1.md) (`COM-CART-010` .. `COM-CART-013`)  
**Input**: Triển khai 3 tính năng nâng cao tiếp theo:
1. `CART-11`: Phân nhóm giỏ theo Shop (`Group by Seller`)
2. `CART-12`: Đổi phân loại hàng trực tiếp (`Change SKU Variant`)
3. `CART-15`: Dọn dẹp món đã thanh toán sau checkout (`Cleanup Checked Out Items`)

---

## 1. Summary & Architectural Alignment

Kế hoạch này mở rộng module Giỏ hàng (Cart Service) để tối ưu hóa trải nghiệm giao diện người dùng theo chuẩn Shopee/Lazada và xây dựng cầu nối hoàn hảo sang Checkout Service:

1. **Domain Layer (`Commerce.Domain`)**:
   * Thêm phương thức `ChangeItemVariant` vào thực thể `Cart.cs`:
     * Đổi `SkuId`, `ProductNameSnapshot`, `UnitPriceSnapshot` của dòng chỉ định.
     * Xử lý trường hợp SKU mới đã có sẵn trong giỏ $\rightarrow$ Tự động gộp số lượng (tối đa 99 chiếc) và loại bỏ dòng trùng.
     * Tự động tính lại `SubtotalAmount`, `ItemCount`, tăng `RowVersion`.
   * Tái sử dụng phương thức `BulkRemoveItems` trong `Cart.cs` để dọn dẹp các món đã thanh toán.

2. **Application Layer (`Commerce.Application`)**:
   * **Request DTOs (`CartRequests.cs`)**:
     * `ChangeCartItemVariantRequest(Guid NewSkuId)`
     * `CleanupCheckedOutItemsRequest(List<Guid> PurchasedCartItemIds)`
   * **Response DTOs (`CartResponses.cs`)**:
     * `ShopCartGroupResponse(Guid SellerId, string ShopName, int DistinctItemCount, int TotalQuantity, decimal ShopSubtotal, IReadOnlyList<CartItemResponse> Items)`
     * `GroupedCartResponse(Guid CartId, Guid BuyerId, int TotalDistinctItems, int TotalQuantity, decimal SubtotalAmount, IReadOnlyList<ShopCartGroupResponse> Shops)`
   * **Service Interface (`ICartService.cs`)**:
     * `Task<GroupedCartResponse> GetGroupedCartAsync(Guid buyerId, CancellationToken ct = default);`
     * `Task<CartDto> ChangeItemVariantAsync(Guid buyerId, Guid cartItemId, ChangeCartItemVariantRequest request, CancellationToken ct = default);`
     * `Task<CartDto> CleanupCheckedOutItemsAsync(Guid buyerId, CleanupCheckedOutItemsRequest request, CancellationToken ct = default);`
   * **Tương tác liên dịch vụ**: Tận dụng `ICatalogClient.GetSkuDetailsAsync` để kiểm tra phân loại mới còn hàng và lấy giá mới nhất.

3. **API Layer (`Commerce.Api/Controllers/Public/CartsController.cs`)**:
   * `GET /api/v1/carts/me/grouped`: Trả về giỏ hàng đã phân nhóm theo Seller/Shop.
   * `PATCH /api/v1/carts/me/items/{cartItemId}/variant`: Đổi phân loại hàng trong giỏ.
   * `POST /api/v1/carts/me/cleanup-checked-out`: Dọn dẹp món đã mua sau khi nhận tín hiệu thanh toán thành công.
   * Toàn bộ endpoints áp dụng `[RequireRole("buyer", "admin", "staff")]` và xác thực qua `this.TryGetUserId()`.

4. **Testing Layer (`tests/Commerce.UnitTests`)**:
   * Bổ sung Unit Tests cho `ChangeItemVariant` (đổi sang SKU mới, gộp số lượng khi trùng).
   * Bổ sung Unit Tests cho `GetGroupedCartAsync` (gom nhóm chuẩn theo Shop).
   * Bổ sung Unit Tests cho `CleanupCheckedOutItemsAsync` (xóa đúng món đã mua).

---

## 2. Technical Context & Constitution Guardrails

* **Clean Architecture**: 100% tuân thủ cấu trúc 4 tầng, Feature Co-location tại `Services/Cart/`.
* **No-Repository Pattern**: Sử dụng trực tiếp `ICommerceDbContext` và `DbSet<Cart>`.
* **Error Rules**: 100% tiếng Anh, Sentence case, mã lỗi `SCREAMING_SNAKE_CASE` (ví dụ: `CART_NOT_FOUND`, `CART_ITEM_NOT_FOUND`, `CATALOG_SKU_INACTIVE`).
* **Database & Concurrency**: Bảo vệ cập nhật đồng thời thông qua `RowVersion` và `last_activity_at`.

---

## 3. Detailed Component Design

### 3.1 Domain Method Design (`Cart.cs`)
```csharp
public CartItem ChangeItemVariant(Guid cartItemId, Guid newSkuId, string newProductName, decimal newUnitPrice)
{
    var currentItem = Items.FirstOrDefault(i => i.Id == cartItemId)
        ?? throw new CommerceDomainException("CART_ITEM_NOT_FOUND", "Cart item not found in the current cart.", 404);

    if (currentItem.SkuId == newSkuId)
    {
        return currentItem; // Không thay đổi nếu cùng SKU
    }

    var existingDuplicate = Items.FirstOrDefault(i => i.SkuId == newSkuId && i.Id != cartItemId);
    if (existingDuplicate != null)
    {
        // SKU mới đã có trong giỏ -> Cộng dồn số lượng vào dòng có sẵn, xóa dòng cũ
        var mergedQuantity = existingDuplicate.Quantity + currentItem.Quantity;
        if (mergedQuantity > 99) mergedQuantity = 99;

        existingDuplicate.Quantity = mergedQuantity;
        existingDuplicate.UnitPriceSnapshot = newUnitPrice;
        existingDuplicate.ProductNameSnapshot = newProductName;
        Items.Remove(currentItem);
        RecalculateTotals();
        return existingDuplicate;
    }

    // Cập nhật thông tin dòng hiện tại
    currentItem.SkuId = newSkuId;
    currentItem.ProductNameSnapshot = newProductName;
    currentItem.UnitPriceSnapshot = newUnitPrice;
    RecalculateTotals();
    return currentItem;
}
```

### 3.2 Application Service Logic (`CartService.cs`)
1. **`GetGroupedCartAsync`**:
   - Truy vấn giỏ hàng `ACTIVE` kèm danh sách `Items`.
   - Gom nhóm `Items` theo `SellerId`:
     ```csharp
     var shopGroups = cart.Items
         .GroupBy(i => i.SellerId)
         .Select(g => new ShopCartGroupResponse(
             SellerId: g.Key,
             ShopName: $"Shop {g.Key.ToString()[..8]}", // Hoặc lấy từ snapshot/User service
             DistinctItemCount: g.Count(),
             TotalQuantity: g.Sum(x => x.Quantity),
             ShopSubtotal: g.Sum(x => x.Quantity * x.UnitPriceSnapshot),
             Items: g.Select(MapToItemResponse).ToList()))
         .ToList();
     ```
2. **`ChangeItemVariantAsync`**:
   - Kiểm tra `NewSkuId` thông qua `_catalogClient.GetSkuDetailsAsync(request.NewSkuId)`.
   - Nếu SKU không khả dụng $\rightarrow$ ném `CATALOG_SKU_INACTIVE`.
   - Gọi `cart.ChangeItemVariant(...)` và lưu xuống database.
3. **`CleanupCheckedOutItemsAsync`**:
   - Gọi `cart.BulkRemoveItems(request.PurchasedCartItemIds)`.
   - Lưu xuống database và trả về giỏ hàng cập nhật.

---

## 4. Verification Plan

1. **Unit Testing**:
   * Kiểm tra đầy đủ các test case: đổi phân loại thành công, cộng dồn khi trùng SKU, kiểm tra phân nhóm theo Shop, xóa đúng danh sách món đã thanh toán.
2. **Build Verification**:
   * Chạy `dotnet build Commerce.sln -c Release` $\rightarrow$ 0 lỗi.
3. **Automated Test Run**:
   * Chạy `dotnet test Commerce.sln -c Release` $\rightarrow$ Đạt 100% pass (từ 16 tests lên 20+ tests).
4. **Manual Testing**:
   * Cập nhật file kịch bản `Commerce.Api.http` và kiểm thử trực tiếp trên Swagger UI.
