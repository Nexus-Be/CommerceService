# CATALOG SERVICE — VOUCHER SKU VALIDATION ALIGNMENT CONTRACT

> **Target Service**: `CatalogService` (Port `8002`, `catalog_db`)  
> **Calling Service**: `CommerceService` (Port `8003`) via `ICatalogClient` (`CatalogClient.cs`)  
> **Protocol**: Synchronous REST over HTTP/JSON  
> **Endpoint**: `POST http://catalog-service:8002/api/v1/internal/variants`  
> **Security Headers**: `X-Internal-Service-Key`, `x-user-id`, W3C `traceparent`  
> **Status**: ✅ RATIFIED & IMPLEMENTED (100% Đồng thuận kỹ thuật)  
> **Last Updated**: 2026-11-17  

---

## 1. Bối cảnh & Đồng thuận Kiến trúc Bounded Context

Trong hệ thống Voucher 3 tầng của CommerceService:
- **Tầng 1 là Voucher Sản phẩm (`ScopeType = Product`)**: Cho phép người bán tạo mã giảm giá gắn riêng cho một hoặc nhiều mã SKU/Variant cụ thể trong gian hàng của mình.
- Sau khi đối chiếu kỹ thuật với CatalogService ngày 2026-11-17, hai bên đã đạt được đồng thuận 100% với 3 nguyên tắc bất biến:
  1. **Giữ nguyên Endpoint Hiện hữu (`POST /api/v1/internal/variants`)**: Không tạo route alias `POST /api/v1/internal/skus/batch` gây phân mảnh API. `CatalogClient.cs` của CommerceService đã và đang tích hợp trực tiếp vào endpoint chuẩn này.
  2. **Rào chắn Bounded Context Tồn kho**: CatalogService **không** quản lý tồn kho vật lý và **không** trả về trường `stockQuantity`. Toàn bộ kiểm tra số lượng khả dụng thuộc quyền quản lý của `FulfillmentService` (Port `8006`). Trường `isPurchasable` của CatalogService phản ánh trạng thái thương mại (`ProductStatus.Active`, `VariantStatus.Active`, và `Price > 0`).
  3. **Chặn Voucher đối với Sản phẩm Đấu giá (`SaleType == Auction`)**: CatalogService trả về trường `saleType`. CommerceService nghiêm cấm áp dụng voucher sản phẩm cho sản phẩm đấu giá nhằm bảo toàn tính toàn vẹn của các lượt bid.

---

## 2. Đặc tả API Thẩm định Variant Hàng loạt (`POST /api/v1/internal/variants`)

### 2.1. Yêu cầu HTTP (Request)
- **Method**: `POST`
- **URL**: `/api/v1/internal/variants`
- **Headers**:
  ```http
  Content-Type: application/json
  X-Internal-Service-Key: <secret_internal_key>
  traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
  ```
- **Body**: Đối tượng chứa danh sách `variantIds` (UUID v4):
  ```json
  {
    "variantIds": [
      "6fa85f64-5717-4562-b3fc-2c963f66afa6",
      "7fa85f64-5717-4562-b3fc-2c963f66afa7",
      "8fa85f64-5717-4562-b3fc-2c963f66afa8"
    ]
  }
  ```

---

### 2.2. Cấu trúc Phản hồi (Response)
- **HTTP Status**: `200 OK`
- **Body**: Danh sách `items` chi tiết kèm mảng `notFoundIds`:
  ```json
  {
    "items": [
      {
        "variantId": "6fa85f64-5717-4562-b3fc-2c963f66afa6",
        "productId": "1fa85f64-5717-4562-b3fc-2c963f66afa1",
        "sellerId": "9fa85f64-5717-4562-b3fc-2c963f66afa9",
        "productName": "Áo Polo Nam Thể Thao Phối Màu",
        "variantName": "Size L - Xanh Navy",
        "skuCode": "POLO-NAVY-L",
        "price": 250000.0,
        "currency": "VND",
        "isPurchasable": true,
        "saleType": "Regular",
        "status": "Active",
        "productStatus": "Active"
      },
      {
        "variantId": "7fa85f64-5717-4562-b3fc-2c963f66afa7",
        "productId": "2fa85f64-5717-4562-b3fc-2c963f66afa2",
        "sellerId": "9fa85f64-5717-4562-b3fc-2c963f66afa9",
        "productName": "Quần Jean Nam Slimfit",
        "variantName": "Size 32 - Đen",
        "skuCode": "JEAN-SLIM-32",
        "price": 450000.0,
        "currency": "VND",
        "isPurchasable": false,
        "saleType": "Regular",
        "status": "Inactive",
        "productStatus": "Active"
      },
      {
        "variantId": "8fa85f64-5717-4562-b3fc-2c963f66afa8",
        "productId": "3fa85f64-5717-4562-b3fc-2c963f66afa3",
        "sellerId": "9fa85f64-5717-4562-b3fc-2c963f66afa9",
        "productName": "Đồng Hồ Cổ Đấu Giá Phiên Bản 1980",
        "variantName": "Nguyên Bản Gold",
        "skuCode": "AUC-WATCH-1980",
        "price": 12000000.0,
        "currency": "VND",
        "isPurchasable": true,
        "saleType": "Auction",
        "status": "Active",
        "productStatus": "Active"
      }
    ],
    "notFoundIds": [
      "99999999-5717-4562-b3fc-2c963f66af99"
    ]
  }
  ```

---

## 3. Bảng Đối chiếu Trường Dữ liệu Chuẩn Giữa Hai Service

`CatalogClient.cs` của CommerceService nhận response từ `POST /api/v1/internal/variants` và map trực tiếp sang model `CatalogProductCheckResult`:

| Trường CatalogService trả về (`BatchVariantItemDto`) | Kiểu dữ liệu | Model nội bộ Commerce (`CatalogProductCheckResult`) | Ý nghĩa nghiệp vụ tại Commerce |
|:---|:---|:---|:---|
| `variantId` | `Guid` (UUID v4) | `SkuId` | Định danh phiên bản sản phẩm |
| `productId` | `Guid` (UUID v4) | `ProductId` | Định danh sản phẩm cha |
| `sellerId` | `Guid` (UUID v4) | `SellerId` | **Đối chiếu quyền sở hữu Shop (`skuInfo.SellerId == currentUserId`)** |
| `productName` | `string` | `ProductName` | Tên hiển thị sản phẩm cha |
| `price` | `decimal?` | `UnitPrice` | **Giá tươi thời gian thực (Real-time Fresh Price)** |
| `isPurchasable` | `bool` | `IsPurchasable` | `true` khi cả Product & Variant đều Active và có giá hợp lệ |
| `saleType` | `string` | `SaleType` | **`"Regular"` hoặc `"Auction"` — dùng để chặn voucher sản phẩm đấu giá** |
| `notFoundIds` | `List<Guid>` | (Tra cứu dictionary) | Xác định các SKU không tồn tại trong Catalog |

---

## 4. Rào chắn Nghiệp vụ Thẩm định tại `CommerceService`

Khi Seller tạo mã voucher sản phẩm (`POST /api/v1/discounts` kèm `ScopeType = Product`), `CommerceService` thực thi nghiêm ngặt 4 rào chắn:

1. **Rào chắn Tồn tại (`CATALOG_SKU_NOT_FOUND`)**:
   - Nếu SKU ID nằm trong `notFoundIds` hoặc không tồn tại trong từ điển phản hồi:
   - Ném lỗi: **HTTP 404 `CATALOG_SKU_NOT_FOUND`**
   - Message: `"Product SKU '{skuId}' was not found in catalog."`

2. **Rào chắn Quyền sở hữu Shop (`VOUCHER_SELLER_SKU_FORBIDDEN`)**:
   - Bắt buộc `skuInfo.SellerId == currentUserId` (trừ tài khoản Quản trị viên `isAdmin`).
   - Ngăn chặn triệt để hành vi Shop A gán voucher của mình vào sản phẩm của Shop B để phá giá hoặc trục lợi.
   - Ném lỗi: **HTTP 403 `VOUCHER_SELLER_SKU_FORBIDDEN`**
   - Message: `"Product SKU '{skuId}' does not belong to your shop."`

3. **Rào chắn Khả dụng Thương mại (`CATALOG_SKU_INACTIVE`)**:
   - Nếu SKU có `isPurchasable == false` (bị xóa, ẩn, tạm khóa hoặc chưa có giá hợp lệ):
   - Ném lỗi: **HTTP 400 `CATALOG_SKU_INACTIVE`**
   - Message: `"Product '{productName}' is no longer available for purchase."`

4. **Rào chắn Chặn Voucher Đấu giá (`VOUCHER_SKU_AUCTION_NOT_ALLOWED`)**:
   - Bắt buộc `skuInfo.SaleType != "Auction"`.
   - Voucher thương mại chỉ áp dụng cho sản phẩm bán thường lẻ (`SaleType == "Regular"`).
   - Ném lỗi: **HTTP 400 `VOUCHER_SKU_AUCTION_NOT_ALLOWED`**
   - Message: `"Voucher cannot be applied to auction products."`

5. **Kiểm soát Biến động giá khi Đặt hàng (`CreateOrderAsync`)**:
   - Khi Buyer bấm xác nhận đặt hàng, `CommerceService` gọi lại `GetSkuBatchDetailsAsync` để lấy giá tươi nhất từ CatalogService.
   - Nếu có sản phẩm tăng giá so với lúc Báo giá (Quote): Chặn thanh toán ngay lập tức (**HTTP 409 `PRICE_CHANGED`**) và cập nhật giỏ hàng để bảo vệ quyền lợi của Buyer.

---

## 5. Trạng thái Đồng thuận & Ký duyệt Kỹ thuật

- Phía **CatalogService**: Đã vận hành ổn định và sẵn sàng 100% tại `POST /api/v1/internal/variants`.
- Phía **CommerceService**: Đã cập nhật `ICatalogClient.cs`, `CatalogClient.cs`, và `DiscountService.cs` đồng bộ 100% với contract này. Bộ kiểm thử đơn vị (`CatalogClientTests`, `DiscountServiceTests`) đã kiểm chứng thành công.
