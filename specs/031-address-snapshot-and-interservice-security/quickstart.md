# Quickstart Guide: Address Snapshot Parity, Checkout Address Integration & S2S Internal Security

**Feature**: `031-address-snapshot-and-interservice-security`  
**Date**: 2026-11-19

---

## 1. Mục Tiêu Kiểm Thử

Hướng dẫn nhanh để lập trình viên và kiểm thử viên kiểm chứng 5 tính năng cốt lõi của SPEC-031:
1. Nạp địa chỉ theo `AddressId` và auto-fallback địa chỉ mặc định từ `UserService`.
2. Tạo báo giá đặt hàng không cần nhập tay địa chỉ (`QuoteCheckoutAsync`).
3. Đổi địa chỉ nhận hàng và tính lại phí ship GHN (`RecalculateQuoteAsync`).
4. Gọi API nội bộ xác minh khiếu nại (`GET /api/v1/internal/orders/dispute-eligibility`).
5. Đồng bộ hóa retry worker cho callback đơn đấu giá.

---

## 2. Lệnh Kiểm Chứng Nhanh (Automated Tests)

### Chạy toàn bộ Unit Tests liên quan
```powershell
# Kiểm thử các phương thức mới của OrderService (Re-quote, DisputeEligibility, Address Resolution)
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~OrderServiceTests"

# Kiểm thử AddressId và Default Address trong AuctionOrderService
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~AuctionOrderServiceTests"

# Kiểm thử UserClient S2S
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~UserClientTests"
```

### Chạy toàn bộ test suite của Service
```powershell
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore
```

---

## 3. Kịch Bản Kiểm Thử Bằng HTTP Requests

### Kịch bản 1: Re-quote phí ship khi đổi địa chỉ Checkout
```http
POST /api/v1/checkouts/quote/{{quoteId}}/recalculate HTTP/1.1
Host: localhost:8003
Authorization: Bearer {{buyerToken}}
Content-Type: application/json

{
  "addressId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
}
```
* **Kỳ vọng:** `200 OK` với `totalAmount` và `shippingFeeAmount` được tính lại theo cước GHN của địa chỉ mới.

### Kịch bản 2: UserService xác minh điều kiện Khiếu nại (Internal S2S)
```http
GET /api/v1/internal/orders/dispute-eligibility?orderOrSubOrderId={{subOrderId}}&reporterId={{buyerId}}&reportedUserId={{sellerId}} HTTP/1.1
Host: localhost:8003
X-Internal-Service-Key: {{COMMERCE_INTERNAL_KEY}}
```
* **Kỳ vọng:** `200 OK`
  ```json
  {
    "isEligible": true,
    "orderType": "SubOrder",
    "buyerId": "...",
    "sellerId": "...",
    "status": "Delivered",
    "deliveredAt": "2026-11-18T10:00:00Z"
  }
  ```
