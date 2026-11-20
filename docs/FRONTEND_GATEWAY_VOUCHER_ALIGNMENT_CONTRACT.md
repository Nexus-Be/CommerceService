# FRONTEND & API GATEWAY — VOUCHER & PROMOTION ALIGNMENT CONTRACT

> **Target Teams**: Frontend (Web, Mobile App / Flutter / React Native), API Gateway / BFF  
> **Source Service**: `CommerceService` (Port `8003`)  
> **Base Route**: `http://commerce-service:8003/api/v1`  
> **Protocol**: Synchronous RESTful Web API (JSON, UTF-8)  
> **Authentication**: Bearer JWT (`Authorization: Bearer <token>`) & Context Headers (`x-user-id`, `x-user-roles`)  
> **Status**: RATIFIED & IMPLEMENTED  
> **Last Updated**: 2026-11-17  

---

## 1. Tổng quan & Triết lý Mô hình (Shopee / TikTok Shop Model)

Hệ thống Voucher & Khuyến mãi của Nexus Commerce Service vận hành theo **chuẩn sàn thương mại điện tử thực tế**:
1. **Khách hàng (Buyer)**: Xem giá sản phẩm trên Catalog bao nhiêu thì khi Checkout thanh toán đúng bấy nhiêu, trừ đi các Voucher hợp lệ và cộng phí vận chuyển. **Khách hàng hoàn toàn không chịu thêm bất kỳ phụ phí sàn nào** (Zero Buyer Markup).
2. **Người bán (Seller)**: Đóng phí hoa hồng sàn 5% (khấu trừ trực tiếp từ tiền bán hàng của shop khi giải ngân ví).
3. **Sàn (Platform)**: Tự tài trợ 100% cho các Voucher toàn sàn (System Voucher).

---

## 2. Danh mục 9 REST Endpoints của `DiscountsController`

Tất cả các endpoint đặt tại route: `/api/v1/discounts`.

```text
GET    /api/v1/discounts/available         # UC-01: Lấy danh sách voucher khả dụng cho Buyer
POST   /api/v1/discounts/validate          # UC-02: Kiểm tra tính hợp lệ của 1 mã voucher
POST   /api/v1/discounts/recommend         # UC-03: Gợi ý combo 3 tầng voucher tối ưu nhất
POST   /api/v1/discounts                   # UC-04: Tạo mới voucher (Admin/Seller)
GET    /api/v1/discounts                   # UC-05: Danh sách voucher phân trang & lọc (Admin/Seller/Staff)
GET    /api/v1/discounts/{discountId}      # UC-06: Xem chi tiết 1 voucher
PUT    /api/v1/discounts/{discountId}      # UC-07: Cập nhật metadata voucher
PATCH  /api/v1/discounts/{discountId}/status # UC-08: Bật/tắt trạng thái voucher
DELETE /api/v1/discounts/{discountId}      # UC-09: Xóa mềm voucher (chỉ khi chưa ai dùng)
```

---

## 3. Chi tiết API dành cho Buyer (Khách hàng)

### 3.1. Lấy danh sách Voucher khả dụng (`GET /api/v1/discounts/available`)
- **Mục đích**: Hiển thị popup "Chọn Voucher" tại màn hình Giỏ hàng hoặc Checkout.
- **Role yêu cầu**: `buyer`
- **Query Parameters**:
  - `sellerId` (optional, UUID): Lọc voucher của shop cụ thể + voucher toàn sàn.
  - `cartItemIds` (optional, string): Danh sách ID món trong giỏ cách nhau bằng dấu phẩy (ví dụ: `uuid1,uuid2`). Hệ thống sẽ tự tính xem đơn hàng có đủ điều kiện áp mã không.
  - `page` (optional, int, default: 1).
  - `pageSize` (optional, int, default: 20).
- **Response `200 OK`**:
  ```json
  {
    "items": [
      {
        "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "code": "FREESHIP30K",
        "name": "Giảm 30K toàn sàn",
        "scopeType": "System",
        "ownerSellerId": null,
        "discountType": "FixedAmount",
        "discountValue": 30000.0,
        "minOrderAmount": 200000.0,
        "maxDiscountAmount": null,
        "startsAt": "2026-11-01T00:00:00Z",
        "endsAt": "2026-11-30T23:59:59Z",
        "isEligible": true,
        "ineligibleReason": null,
        "estimatedDiscountAmount": 30000.0
      },
      {
        "id": "4fa85f64-5717-4562-b3fc-2c963f66afa7",
        "code": "SHOP50K",
        "name": "Giảm 50K đơn từ 500K",
        "scopeType": "Shop",
        "ownerSellerId": "9fa85f64-5717-4562-b3fc-2c963f66afa9",
        "discountType": "FixedAmount",
        "discountValue": 50000.0,
        "minOrderAmount": 500000.0,
        "maxDiscountAmount": null,
        "startsAt": "2026-11-01T00:00:00Z",
        "endsAt": "2026-11-30T23:59:59Z",
        "isEligible": false,
        "ineligibleReason": "Minimum order amount 500,000 VND not met.",
        "estimatedDiscountAmount": 0.0
      }
    ],
    "pagination": {
      "pageIndex": 1,
      "pageSize": 20,
      "totalCount": 2,
      "totalPages": 1,
      "hasPreviousPage": false,
      "hasNextPage": false
    }
  }
  ```
- **UI Action**: Nếu `isEligible == false`, làm mờ nút chọn (disable checkbox) và hiển thị tooltip dòng chữ `ineligibleReason`.

---

### 3.2. Kiểm tra mã nhập tay (`POST /api/v1/discounts/validate`)
- **Mục đích**: Khi khách hàng gõ mã coupon vào ô input và bấm "Áp dụng".
- **Role yêu cầu**: `buyer`
- **Request Body**:
  ```json
  {
    "code": "SUMMER50",
    "cartItemIds": [
      "1fa85f64-5717-4562-b3fc-2c963f66afa1",
      "2fa85f64-5717-4562-b3fc-2c963f66afa2"
    ]
  }
  ```
- **Response `200 OK`**:
  ```json
  {
    "isValid": true,
    "discountId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "code": "SUMMER50",
    "scopeType": "Shop",
    "estimatedDiscountAmount": 50000.0
  }
  ```
- **Lỗi thường gặp**:
  - `404 VOUCHER_NOT_FOUND`: Mã không tồn tại hoặc đã hết hạn.
  - `400 VOUCHER_NOT_APPLICABLE`: Mã không áp dụng được cho các sản phẩm trong giỏ (kèm lý do trong trường `message`).

---

### 3.3. Tự động gợi ý Combo Voucher tối ưu (`POST /api/v1/discounts/recommend`)
- **Mục đích**: Nút "Áp dụng mã giảm giá tốt nhất" (Best Savings Auto-Apply).
- **Request Body**:
  ```json
  {
    "cartItemIds": [
      "1fa85f64-5717-4562-b3fc-2c963f66afa1",
      "2fa85f64-5717-4562-b3fc-2c963f66afa2"
    ]
  }
  ```
- **Response `200 OK`**:
  ```json
  {
    "recommendedVoucherCodes": [
      "SHOP20K",
      "SYS30K"
    ],
    "savingsBreakdown": {
      "shopDiscount": 20000.0,
      "systemDiscount": 30000.0,
      "totalSavings": 50000.0
    }
  }
  ```
- **UI Action**: Tự động tick chọn các mã trong `recommendedVoucherCodes` và đưa vào báo giá Checkout.

---

## 4. Tích hợp Checkout & Đặt hàng (`OrdersController`)

### 4.1. Báo giá Checkout đa Shop (`POST /api/v1/checkouts/quote`)
Frontend gửi danh sách mã voucher khách đã chọn trong mảng `voucherCodes`:
- **Request Body**:
  ```json
  {
    "cartItemIds": ["1fa85f64-5717-4562-b3fc-2c963f66afa1"],
    "shippingAddress": {
      "receiverName": "Nguyen Van A",
      "contactPhone": "0901234567",
      "street": "123 Le Loi",
      "ward": "Ben Nghe",
      "district": "Quan 1",
      "province": "Ho Chi Minh",
      "ghnDistrictId": 1442,
      "ghnWardCode": "20101"
    },
    "contactPhone": "0901234567",
    "paymentMethod": "Wallet",
    "voucherCodes": ["PROD10K", "SHOP20K", "SYS30K"]
  }
  ```

- **Response `200 OK`**:
  ```json
  {
    "quoteId": "8fa85f64-5717-4562-b3fc-2c963f66afa8",
    "grandTotal": 370000.0,
    "walletBalance": 1500000.0,
    "shops": [
      {
        "sellerId": "9fa85f64-5717-4562-b3fc-2c963f66afa9",
        "shopName": "Shop A Official",
        "items": [...],
        "itemsAmount": 400000.0,
        "productDiscountAmount": 10000.0,
        "shopDiscountAmount": 20000.0,
        "systemDiscountAmount": 30000.0,
        "discountAmount": 60000.0,
        "shippingFeeAmount": 30000.0,
        "totalAmount": 370000.0
      }
    ],
    "appliedVouchers": ["PROD10K", "SHOP20K", "SYS30K"],
    "warnings": null,
    "expiredAt": "2026-11-17T00:45:00Z"
  }
  ```
- **Lưu ý về Warnings**: Nếu người dùng truyền mã không hợp lệ hoặc đã hết lượt, hệ thống **không báo lỗi làm đứt luồng đặt hàng**, mà sẽ trả về danh sách cảnh báo trong mảng `warnings` (ví dụ: `["Voucher 'EXPIRED_CODE' could not be applied."]`) để Frontend hiển thị thông báo vàng/toast nhắc nhở.

### 4.2. Tạo đơn hàng (`POST /api/v1/orders`)
Frontend gửi `quoteId` đã nhận từ bước báo giá:
```json
{
  "quoteId": "8fa85f64-5717-4562-b3fc-2c963f66afa8",
  "paymentMethod": "Wallet",
  "shippingAddress": "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
  "contactPhone": "0901234567"
}
```

---

## 5. Chi tiết API dành cho Seller & Admin (Quản trị Voucher)

### 5.1. Tạo mới Voucher (`POST /api/v1/discounts`)
- **Roles**: `admin` (tạo mã toàn sàn hoặc shop), `seller` (chỉ tạo mã cho shop mình).
- **Request Body**:
  ```json
  {
    "code": "SALE1111",
    "name": "Khuyến mãi Lễ độc thân",
    "scopeType": "Shop",
    "discountType": "Percentage",
    "discountValue": 10.0,
    "minOrderAmount": 300000.0,
    "maxDiscountAmount": 50000.0,
    "maxUsesTotal": 1000,
    "maxUsesPerUser": 1,
    "isVisible": true,
    "startsAt": "2026-11-11T00:00:00Z",
    "endsAt": "2026-11-12T00:00:00Z",
    "scopes": []
  }
  ```
- **Nếu tạo mã Sản phẩm (`ScopeType = Product`)**, bắt buộc truyền danh sách SKU trong `scopes`:
  ```json
  "scopes": [
    {
      "scopeRefType": "Sku",
      "scopeRefId": "6fa85f64-5717-4562-b3fc-2c963f66afa6"
    }
  ]
  ```

### 5.2. Cập nhật & Bật/Tắt trạng thái
- **Cập nhật thông tin (`PUT /api/v1/discounts/{id}`)**: Chỉ cho phép sửa tên, hạn chót (`endsAt`), hạn mức dùng (`maxUsesTotal`, `maxUsesPerUser`), cờ hiển thị (`isVisible`). Cấm sửa giá trị giảm tiền nếu voucher đã phát sinh giao dịch.
- **Bật/Tắt (`PATCH /api/v1/discounts/{id}/status`)**: Body: `{"status": "Inactive"}` (tạm dừng) hoặc `{"status": "Active"}` (kích hoạt lại).

---

## 6. Ma trận Mã lỗi Chuẩn Hệ thống (Error Matrix)

Tất cả các lỗi trả về theo chuẩn RFC 9110 Problem Details:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "errorCode": "VOUCHER_STACKING_CONFLICT",
  "message": "Multiple system vouchers cannot be applied to the same order.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

| Mã lỗi (`errorCode`) | HTTP Status | Diễn giải hiển thị UI |
| :--- | :---: | :--- |
| `SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED` | 400 | "Mỗi Shop chỉ được áp dụng tối đa 1 mã giảm giá (Mã SP hoặc Mã Shop)." |
| `VOUCHER_NOT_FOUND` | 404 | "Mã giảm giá không tồn tại hoặc đã hết hạn." |
| `VOUCHER_NOT_APPLICABLE` | 400 | "Mã giảm giá không thỏa mãn điều kiện đơn hàng tối thiểu." |
| `VOUCHER_STACKING_CONFLICT` | 400 | "Không thể áp dụng đồng thời 2 mã giảm giá cùng loại." |
| `VOUCHER_USAGE_LIMIT_EXCEEDED` | 400 | "Mã giảm giá này đã hết lượt sử dụng trên hệ thống." |
| `VOUCHER_USER_LIMIT_EXCEEDED` | 400 | "Bạn đã sử dụng hết số lần cho phép của mã giảm giá này." |
| `VOUCHER_ALREADY_IN_USE` | 400 | "Không thể xóa hoặc thay đổi giá trị của mã đã có người sử dụng." |
| `SELLER_ACTION_RESTRICTED` | 403 | "Tài khoản người bán đang bị giới hạn quyền quản lý khuyến mãi." |
| `VOUCHER_SELLER_SKU_FORBIDDEN` | 403 | "Bạn không có quyền tạo khuyến mãi cho sản phẩm của shop khác." |
