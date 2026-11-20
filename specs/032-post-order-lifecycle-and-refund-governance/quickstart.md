# Quickstart Guide: Post-Order Lifecycle, Address Modification and Refund Governance

**Feature Branch**: `032-post-order-lifecycle-and-refund-governance`  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  

---

## 1. Automated Verification Commands

```bash
# 1. Compile solution with 0 warnings, 0 errors
dotnet build Commerce.sln -c Release --no-restore

# 2. Execute all unit tests
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore
```

---

## 2. Key Scenarios for Testing

### Scenario 1: Post-Order Address Change on Paid SubOrder
1. Đặt đơn hàng thành công qua `QuoteCheckout` và `CreateOrder`. Đơn ở trạng thái `Paid`.
2. Gọi `PATCH /api/v1/sub-orders/{subOrderId}/shipping-address` với `AddressId` mới gần hơn (`newShippingFee <= oldShippingFee`).
3. Xác nhận trả về 200 OK với địa chỉ mới.
4. Thử đổi sang địa chỉ xa hơn (`newShippingFee > oldShippingFee`), xác nhận nhận lỗi HTTP 400 `SHIPPING_FEE_INCREASED`.

### Scenario 2: Refund Governance Flow
1. Đơn `SubOrder` ở trạng thái `Delivered`.
2. Khách hàng gửi yêu cầu refund `POST /api/v1/sub-orders/{subOrderId}/refund`.
3. Gửi tiếp lần 2 $\rightarrow$ Xác nhận nhận HTTP 409 `REFUND_REQUEST_ALREADY_EXISTS`.
4. Người bán gọi `POST /api/v1/refunds/{refundId}/approve` $\rightarrow$ Đơn chuyển sang `Refunded`, `OrderRefund.Status = Completed`.

### Scenario 3: Auction Order Cancel & Timeline
1. Đơn đấu giá ở trạng thái `Paid` (chưa seller accept).
2. Người mua gọi `GET /api/v1/auction-orders/{id}/shipments/timeline` $\rightarrow$ Nhận các mốc lịch sử.
3. Người mua gọi `POST /api/v1/auction-orders/{id}/cancel` $\rightarrow$ Đơn chuyển `Cancelled`, ví được hoàn tiền.
