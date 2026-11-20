# Quickstart Validation Guide: Core Remediation, Security Hardening & Essential Endpoints

**Feature Branch**: `030-core-remediation-and-essential-endpoints`  
**Created**: 2026-11-18  
**Target Service**: `CommerceService`

---

## 1. Prerequisites

1. .NET 8.0 SDK installed.
2. PostgreSQL 16 (Local port `5435`, Database `commerce_db`, User `postgres`).
3. Solution dependencies restored (`dotnet restore Commerce.sln`).

---

## 2. Test Execution & Automated Verification

### 2.1. Compile the entire solution in Release mode
```powershell
dotnet build Commerce.sln -c Release --no-restore
```
*Expected Outcome*: `Build succeeded. 0 Warning(s) 0 Error(s)`.

### 2.2. Execute Core Unit & Contract Tests
```powershell
# Chạy toàn bộ test suites của CommerceService
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj -c Release --no-restore
```
*Expected Outcome*: `Passed! - Failed: 0, Passed: >= 350, Skipped: 10, Total: >= 360`.

### 2.3. Execute Database Schema Consistency Test
```powershell
# Kiểm tra model snapshot khớp 100% với migrations
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~MigrationConsistencyTests" -c Release --no-restore
```
*Expected Outcome*: `Snapshot_MatchesCurrentModel` PASSED.

---

## 3. End-to-End Validation Scenarios

### Scenario 1: Hủy Đơn hàng mẹ và tự động gửi lệnh hủy sang GHN (Fulfillment)
1. Tạo một đơn hàng mẹ gồm 2 đơn con: Shop 1 và Shop 2.
2. Cập nhật Shop 1 sang `Confirmed` và gán `TrackingCode = "GHN-TEST-12345"`.
3. Gọi API hủy đơn mẹ:
   ```http
   POST /api/v1/orders/{orderId}/cancel
   Authorization: Bearer <buyer_token>
   Content-Type: application/json

   {
     "reasonCode": "BuyerChangedMind",
     "reasonText": "I found a better deal."
   }
   ```
4. **Kiểm tra**:
   - `_fulfillmentClient.CancelShipmentAsync` được gọi với `subOrderId` của Shop 1.
   - Trạng thái `Order.Status` = `Cancelled`, các `SubOrders` = `Cancelled`.
   - Nếu thanh toán ví, số tiền ví được hoàn trả đủ 100%.

### Scenario 2: Tra cứu danh sách đơn đấu giá của Người mua
1. Gọi API:
   ```http
   GET /api/v1/auction-orders/me?page=1&pageSize=10&status=PendingPayment
   Authorization: Bearer <buyer_token>
   ```
2. **Kiểm tra**:
   - HTTP Status `200 OK`.
   - Body trả về danh sách `items` chỉ chứa các đơn của người mua này.
   - Mỗi item có trường `depositAmount` phản ánh đúng số tiền cọc đấu giá.

### Scenario 3: Ngăn chặn IDOR khi áp dụng Voucher
1. Đăng nhập tài khoản `Buyer B`.
2. Lấy `cartItemId` thuộc giỏ hàng của `Buyer A`.
3. Gọi API kiểm tra voucher:
   ```http
   POST /api/v1/discounts/validate
   Authorization: Bearer <buyer_b_token>
   Content-Type: application/json

   {
     "code": "SHOP20K",
     "cartItemIds": ["<cart_item_id_of_buyer_a>"]
   }
   ```
4. **Kiểm tra**:
   - HTTP Status `400 Bad Request`.
   - Mã lỗi `VAL_INVALID_INPUT`, thông báo `"No valid cart items provided for voucher validation."`.

### Scenario 4: Admin / Staff tra cứu chi tiết đơn hàng của người khác
1. Đăng nhập tài khoản có role `admin` hoặc `staff`.
2. Gọi API tra cứu đơn hàng của người dùng bất kỳ:
   ```http
   GET /api/v1/orders/{orderId}
   Authorization: Bearer <admin_or_staff_token>
   ```
3. **Kiểm tra**:
   - HTTP Status `200 OK` (thay vì bị lỗi `403 FORBIDDEN`).
