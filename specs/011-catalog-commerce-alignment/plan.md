# Implementation Plan: Catalog and Commerce Contract Alignment

**Branch**: `011-catalog-commerce-alignment` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/011-catalog-commerce-alignment/spec.md`  
**Constitution Authority**: `.specify/memory/constitution.md` (Version 1.3.0 / Project Rules 2026-10-30)  

---

## 1. Summary

Kế hoạch tái cấu trúc và đồng bộ hóa 100% hợp đồng tích hợp giữa `CatalogService` (Port 8002) và `CommerceService` (Port 8003):
1. **Infrastructure Layer (`CatalogClient.cs`)**: Cập nhật DTO `CatalogVariantRawItem` đón nhận `IsPurchasable` và `decimal? Price`. Xóa bỏ hoàn toàn logic kiểm tra giá cục bộ (`price > 0`) và xóa bỏ triệt để các khối code fallback mock 100.000 VNĐ / SellerId ảo; chuyển sang ném ngoại lệ chuẩn RFC 9110 HTTP 503 `CATALOG_SERVICE_UNAVAILABLE`.
2. **Application Layer (`CartService.cs`)**: Triển khai cơ chế làm xám giỏ hàng (`Cart Gray-out`) khi tải giỏ hàng (`GET /api/v1/carts/me` và `GetGroupedCartAsync`) bằng cách gọi song song (`Task.WhenAll`) giữa Catalog và Fulfillment. Áp dụng quy tắc ưu tiên lý do vô hiệu hóa: `PRODUCT_NOT_FOUND` > `PRODUCT_INACTIVE` > `OUT_OF_STOCK`. Cập nhật `ValidateCartItemsAsync` lưu tức thì snapshot giá mới vào PostgreSQL.
3. **Test Suite (`Commerce.UnitTests`)**: Cập nhật và bổ sung các ca kiểm thử trong `CatalogClientTests.cs` và `CartServiceTests.cs`, đảm bảo toàn bộ 88+ unit tests tiếp tục pass 100%.

---

## 2. Technical Context

- **Language/Version**: C# 12 / .NET 8.0 (`net8.0`).
- **Primary Dependencies**: ASP.NET Core 8, EF Core 8 (Npgsql.EntityFrameworkCore.PostgreSQL), Typed `HttpClient`, `System.Text.Json`.
- **Storage**: PostgreSQL (`commerce_db:5435`).
- **Testing**: xUnit, FluentAssertions, Moq, `Microsoft.EntityFrameworkCore.InMemory`.
- **Target Platform**: Port `8003` (Container) / Local Development.
- **Constraints**: Tuân thủ nghiêm ngặt Inward-Only, 100% RESTful Web API, Zero Message Broker, và chuẩn thông báo lỗi RFC 9110 (100% English sentence-case, `SCREAMING_SNAKE_CASE` code).

---

## 3. Constitution Check

| Nguyên Tắc Hiến Pháp | Đánh Giá Tuân Thủ | Giải Pháp Thực Thi |
| :--- | :---: | :--- |
| **I. Clean Architecture & No-Repository** | ✅ TUÂN THỦ | Không tạo repository mới. Tương tác trực tiếp qua `ICommerceDbContext` và `ICatalogClient`. |
| **II. Bãi bỏ RabbitMQ / Saga / gRPC** | ✅ TUÂN THỦ | 100% RESTful Web API đồng bộ giữa CommerceService và CatalogService. |
| **III. Port & Database Bất Biến** | ✅ TUÂN THỦ | Commerce chạy Port `8003`, DB `5435`. Catalog chạy Port `8002`. |
| **IV. Mã Lỗi Chuẩn (RFC 9110)** | ✅ TUÂN THỦ | Sử dụng mã lỗi chuẩn: `CATALOG_SERVICE_UNAVAILABLE` (503), `CATALOG_VARIANT_MISMATCH` (400), `CATALOG_SKU_NOT_FOUND` (404), `PRICE_CHANGED` (409). |
| **V. Inward-Only & Self-Contained** | ✅ TUÂN THỦ | Không chứa liên kết hay import trỏ ngược ra ngoài repository CommerceService. |

---

## 4. Architecture & Layer Mapping

```mermaid
flowchart TD
    subgraph Commerce Api [Port 8003]
        CartsCtrl["CartsController.cs\nGET /api/v1/carts/me\nPOST /api/v1/carts/me/validate\nPATCH /api/v1/carts/me/items/{id}/variant"]
        OrdersCtrl["OrdersController.cs\nPOST /api/v1/orders"]
    end

    subgraph Commerce Application
        CartSvc["CartService.cs\n(Parallel Task.WhenAll)"]
        OrderSvc["OrderService.cs\n(Asymmetric Price Drift)"]
        ICatalog["ICatalogClient"]
        IFulfill["IFulfillmentClient"]
        IDbContext["ICommerceDbContext"]
    end

    subgraph Commerce Infrastructure
        CatalogClient["CatalogClient (Typed HttpClient)"]
        CatalogClient -.->|Header| AuthHeader["X-Internal-Service-Key"]
    end

    subgraph External Catalog Service [Port 8002]
        BatchVariants["POST /api/v1/internal/variants\n(Source of Truth: isPurchasable, price)"]
        ValidateSwitch["POST /api/v1/internal/catalog/skus/validate-variant-switch\n(Validate same parent product)"]
    end

    CartsCtrl --> CartSvc
    OrdersCtrl --> OrderSvc
    CartSvc --> ICatalog
    CartSvc --> IFulfill
    CartSvc --> IDbContext
    OrderSvc --> ICatalog
    OrderSvc --> IDbContext
    CatalogClient -.->|Implements| ICatalog
    CatalogClient -->|POST [variantIds]| BatchVariants
    CatalogClient -->|POST {currentSkuId, newSkuId}| ValidateSwitch
```

---

## 5. File Modification Breakdown

### Phase 1: Infrastructure Layer (`Commerce.Infrastructure`)
- [`Commerce.Infrastructure/Services/Catalog/CatalogClient.cs`](file:///d:/CommerceService/Commerce.Infrastructure/Services/Catalog/CatalogClient.cs):
  - **`CatalogVariantRawItem`**:
    - Thêm `public bool IsPurchasable { get; set; }`.
    - Chuyển `public decimal Price` sang `public decimal? Price`.
  - **`GetSkuBatchDetailsAsync`**:
    - Xóa bỏ logic tính `isPurchasable = ... && item.Price > 0`. Thay bằng gán trực tiếp: `bool isPurchasable = item.IsPurchasable` và `decimal unitPrice = item.Price ?? 0m`.
    - Xóa bỏ hoàn toàn khối `catch` sinh mock fallback `100000m` và `11111111-1111-1111-1111-111111111111`.
    - Ném `CommerceDomainException("CATALOG_SERVICE_UNAVAILABLE", "Product catalog service is temporarily unreachable.", 503)` khi Catalog trả về mã lỗi HTTP không thành công (`!response.IsSuccessStatusCode`) hoặc lỗi mạng.
  - **`ValidateVariantSwitchAsync`**:
    - Xóa bỏ khối fallback mock `100000m`.
    - Ném `CommerceDomainException("CATALOG_SERVICE_UNAVAILABLE", ..., 503)` khi mất kết nối.

---

### Phase 2: Application Layer (`Commerce.Application`)
- [`Commerce.Application/Services/Cart/CartService.cs`](file:///d:/CommerceService/Commerce.Application/Services/Cart/CartService.cs):
  - **`GetActiveCartByBuyerIdAsync` & `GetOrCreateActiveCartAsync`**:
    - Gọi song song:
      ```csharp
      var stockTask = _fulfillmentClient.BatchCheckStockAsync(skuIds, cancellationToken);
      var catalogTask = _catalogClient.GetSkuBatchDetailsAsync(skuIds, cancellationToken);
      await Task.WhenAll(stockTask, catalogTask);
      ```
    - Truyền cả `stockResults` và `catalogResults` vào `MapToDto`.
  - **`GetGroupedCartAsync`**:
    - Áp dụng kiểm tra song song Catalog và Fulfillment để gán đúng `IsAvailable` và `DisabledReason` trên từng `CartItemResponse`.
  - **`MapToDto`**:
    - Nhận thêm tham số `IReadOnlyDictionary<Guid, CatalogProductCheckResult>? catalogResults`.
    - Áp dụng quy tắc ưu tiên lý do vô hiệu hóa:
      ```csharp
      if (catalogInfo == null)
      {
          isAvailable = false;
          disabledReason = "PRODUCT_NOT_FOUND";
      }
      else if (!catalogInfo.IsPurchasable)
      {
          isAvailable = false;
          disabledReason = "PRODUCT_INACTIVE";
      }
      else if (stockInfo != null && (!stockInfo.IsAvailable || stockInfo.AvailableStock <= 0))
      {
          isAvailable = false;
          disabledReason = "OUT_OF_STOCK";
      }
      ```
  - **`ValidateCartItemsAsync` (`POST /api/v1/carts/me/validate`)**:
    - Cập nhật `item.UnitPriceSnapshot = catalogSku.UnitPrice` và `item.UpdatedAt = DateTimeOffset.UtcNow` ngay khi phát hiện chênh lệch giá, lưu xuống PostgreSQL.
    - Bổ sung `CartItemIssue` loại `"PRODUCT_INACTIVE"` nếu `!catalogSku.IsPurchasable`.

---

### Phase 3: Test Suite Updates (`tests/Commerce.UnitTests`)
- [`tests/Commerce.UnitTests/Infrastructure/CatalogClientTests.cs`](file:///d:/CommerceService/tests/Commerce.UnitTests/Infrastructure/CatalogClientTests.cs):
  - Thêm test case xác thực `GetSkuBatchDetailsAsync` map `IsPurchasable` trực tiếp từ Catalog.
  - Thêm test case xác thực xử lý `price: null` an toàn không crash JSON parser.
  - Thêm test case xác thực ném lỗi HTTP 503 `CATALOG_SERVICE_UNAVAILABLE` khi Catalog trả về lỗi 500 (không trả mock).
  - Thêm test case xác thực `ValidateVariantSwitchAsync` ném 503 khi Catalog mất kết nối.
- [`tests/Commerce.UnitTests/Application/CartServiceTests.cs`](file:///d:/CommerceService/tests/Commerce.UnitTests/Application/CartServiceTests.cs):
  - Thêm test case kiểm tra `GetActiveCartByBuyerIdAsync` gán `isAvailable = false` và `disabledReason = "PRODUCT_INACTIVE"` khi Catalog báo `IsPurchasable = false`.
  - Thêm test case kiểm tra thứ tự ưu tiên gán nhãn làm xám giỏ hàng.
- [`tests/Commerce.UnitTests/Contracts/CatalogContractSchemaTests.cs`](file:///d:/CommerceService/tests/Commerce.UnitTests/Contracts/CatalogContractSchemaTests.cs):
  - Đảm bảo 100% tests đối chiếu OpenAPI contract schema tiếp tục pass.

---

### Phase 4: Final Verification
- Biên dịch toàn bộ solution: `dotnet build Commerce.sln -c Release`.
- Chạy 100% unit tests: `dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release`.
