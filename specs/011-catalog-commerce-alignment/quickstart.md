# Quickstart Validation Guide: Catalog and Commerce Contract Alignment

**Feature**: `specs/011-catalog-commerce-alignment`  
**Date**: 2026-11-03  
**Target Environment**: Development / Staging / CI Test Suite  

---

## 1. Prerequisites & Services

1. **CommerceService**: Port `8003` (Container) / Local Development.
2. **CatalogService**: Port `8002` (Single Source of Truth).
3. **Internal Auth Header**: `X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`.

---

## 2. Validation Scenarios

### Scenario 1: Kiểm Tra Cơ Chế Làm Xám Giỏ Hàng (`Cart Gray-out`)
- **Mục tiêu**: Đảm bảo khi sản phẩm bị chuyển sang `IsPurchasable: false` bên Catalog, món hàng trong giỏ của người mua tự động bị làm xám (`isAvailable: false`, `disabledReason: "PRODUCT_INACTIVE"`).
- **Thực hiện**:
  ```http
  GET /api/v1/carts/me HTTP/1.1
  Host: localhost:8003
  Authorization: Bearer <buyer-jwt-token>
  ```
- **Kỳ vọng (`200 OK`)**:
  ```json
  {
    "items": [
      {
        "skuId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "productName": "Áo Sơ Mi Nam Oxford",
        "isAvailable": false,
        "disabledReason": "PRODUCT_INACTIVE"
      }
    ]
  }
  ```

---

### Scenario 2: Kiểm Tra Cơ Chế Chịu Lỗi Fail-Closed (Không Sinh Giá Ảo 100k)
- **Mục tiêu**: Đảm bảo khi CatalogService bị tắt hoặc trả về lỗi 5xx, CommerceService trả về HTTP 503 chuẩn RFC 9110, tuyệt đối không âm thầm sinh giá mock 100.000 VNĐ.
- **Thực hiện (Giả lập Catalog Service Down)**:
  ```http
  POST /api/v1/carts/me/validate HTTP/1.1
  Host: localhost:8003
  Authorization: Bearer <buyer-jwt-token>
  ```
- **Kỳ vọng (`503 Service Unavailable`)**:
  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.6.4",
    "title": "Service Unavailable",
    "status": 503,
    "errorCode": "CATALOG_SERVICE_UNAVAILABLE",
    "detail": "Product catalog service is temporarily unreachable."
  }
  ```

---

### Scenario 3: Xác Thực Đổi Phân Loại Sản Phẩm Trong Giỏ (`Variant Switch`)
- **Mục tiêu**: Đảm bảo việc đổi sang phân loại thuộc sản phẩm khác bị chặn với HTTP 400 `CATALOG_VARIANT_MISMATCH`.
- **Thực hiện**:
  ```http
  PATCH /api/v1/carts/me/items/3fa85f64-5717-4562-b3fc-2c963f66afa6/variant HTTP/1.1
  Host: localhost:8003
  Authorization: Bearer <buyer-jwt-token>
  Content-Type: application/json

  {
    "newSkuId": "7ca85f64-5717-4562-b3fc-2c963f66afa7"
  }
  ```
- **Kỳ vọng khi sai sản phẩm cha (`400 Bad Request`)**:
  ```json
  {
    "status": 400,
    "errorCode": "CATALOG_VARIANT_MISMATCH",
    "detail": "New SKU does not belong to the same parent product."
  }
  ```

---

### Scenario 4: Chặn Tăng Giá Khi Đặt Hàng (`Asymmetric Price Drift`)
- **Mục tiêu**: Khi sản phẩm trong giỏ bị tăng giá trên Catalog, đơn hàng bị chặn và giỏ hàng được cập nhật giá mới.
- **Thực hiện**:
  ```http
  POST /api/v1/orders HTTP/1.1
  Host: localhost:8003
  Authorization: Bearer <buyer-jwt-token>
  Content-Type: application/json

  {
    "cartItemIds": ["3fa85f64-5717-4562-b3fc-2c963f66afa6"],
    "paymentMethod": "Wallet"
  }
  ```
- **Kỳ vọng khi tăng giá (`409 Conflict`)**:
  ```json
  {
    "status": 409,
    "errorCode": "PRICE_CHANGED",
    "detail": "Price of 'Áo Sơ Mi Nam Oxford' has increased from 200,000 to 250,000. Cart prices have been refreshed. Please review your order."
  }
  ```

---

## 3. Lệnh Chạy Bộ Kiểm Thử Tự Động (Automated Test Execution)

```powershell
# Chạy bộ Unit Tests xác thực hợp đồng và logic giỏ hàng
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release
```
- Kết quả mong đợi: Toàn bộ các test suite `CatalogClientTests`, `CartServiceTests` và `CatalogContractSchemaTests` vượt qua 100% với 0 lỗi.
