# Data Model Specification: Frontend Alignment & Integration Resilience

**Feature**: `029-fe-alignment-and-resilience-integration`  
**Target Schema**: PostgreSQL (`commerce_db:5435`)  

---

## 1. Entities & Data Schema Changes

### 1.1 New Entity: `NotificationOutboxItem`
Bảng lưu trữ sự kiện thông báo theo Transactional Outbox Pattern:

| Column Name | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `UUID` | PK | Mã định danh bản ghi outbox |
| `EventId` | `UUID` | Unique, Not Null | Id sự kiện duy nhất gửi sang NotificationService (`sourceEventId`) |
| `Kind` | `VARCHAR(60)` | Not Null | Loại thông báo, ví dụ: `"order-payments"` |
| `SubOrderId` | `UUID` | Index, Not Null | Mã đơn hàng con |
| `SellerUserId` | `UUID` | Not Null | Mã định danh người bán (User ID) |
| `PayloadJson` | `TEXT` | Nullable | Nội dung request body đã đóng băng (chỉ có khi đủ dữ liệu) |
| `Status` | `INTEGER` | Not Null, Index | Trạng thái xử lý (enum `NotificationOutboxStatus`) |
| `AttemptCount` | `INTEGER` | Default `0` | Số lần đã thử gửi |
| `NextRetryAt` | `TIMESTAMPTZ` | Index, Not Null | Thời điểm được phép thử lại tiếp theo |
| `LockedUntil` | `TIMESTAMPTZ` | Nullable | Thời gian hết hạn của atomic lease/lock |
| `LastError` | `VARCHAR(500)` | Nullable | Thông điệp lỗi gần nhất |
| `SentAt` | `TIMESTAMPTZ` | Nullable | Thời điểm gửi thành công |
| `GiveUpAt` | `TIMESTAMPTZ` | Not Null | Hạn chót từ bỏ (mặc định sau 24h) |
| `CreatedAt` | `TIMESTAMPTZ` | Not Null | Thời điểm tạo bản ghi |
| `UpdatedAt` | `TIMESTAMPTZ` | Not Null | Thời điểm cập nhật |
| `IsDeleted` | `BOOLEAN` | Default `false` | Cờ xóa mềm |

### 1.2 New Enum: `NotificationOutboxStatus`
- `Pending` (0): Dữ liệu payload đã sẵn sàng, chờ worker gửi.
- `AwaitingData` (1): Đang chờ truy vấn `ShopId` từ UserService trước khi đóng băng payload.
- `Sent` (2): Đã gửi thành công sang NotificationService.
- `NeedsAttention` (3): Quá hạn 24h hoặc lỗi nghiêm trọng, cần người vận hành can thiệp.

### 1.3 Changes to Entity: `SubOrder`
Bổ sung các cột mới:
- `ShopId` (`UUID`, Nullable): Seller profile id của shop (khác với seller userId), được lưu lại sau lần đầu tra cứu thành công từ UserService.
- `SellerRespondDeadlineAt` (`TIMESTAMPTZ`, Nullable): Mốc thời gian 24h nhắc nhở Seller phản hồi chuẩn bị đơn.

### 1.4 Changes to Entity: `AuctionOrder`
Bổ sung các cột mới:
- `DepositId` (`UUID`, Nullable): Mã định danh khoản ký quỹ/đặt cọc tại WalletService.
- `WeightGramsSnapshot` (`INTEGER`, Default `0`): Khối lượng sản phẩm lưu tại thời điểm chốt đơn để tính phí vận chuyển chính xác.
- `PayoutCompletedAt` (`TIMESTAMPTZ`, Nullable): Thời điểm hoàn tất giải ngân tiền bán đấu giá cho Seller.

### 1.5 Changes to Entity: `OrderStatusHistory`
Bổ sung các cột mới:
- `AuctionCallbackStatus` (`VARCHAR(50)`, Nullable): Trạng thái callback gửi về AuctionService (`"Paid"`, `"PaymentTimedOut"`).
- `AuctionCallbackSentAt` (`TIMESTAMPTZ`, Nullable): Thời điểm callback đấu giá gửi thành công sang AuctionService.

---

## 2. Database Migration Requirements
- Migration Name: `20261117200000_AddNotificationOutboxAndAuctionSyncFields`
- Bao gồm:
  - `CreateTable: NotificationOutbox` với các index trên `EventId` (Unique), `SubOrderId`, và Composite Index `(Status, NextRetryAt)`.
  - `AddColumn: SubOrders.ShopId` và `SubOrders.SellerRespondDeadlineAt`.
  - `AddColumn: AuctionOrders.DepositId`, `AuctionOrders.WeightGramsSnapshot`, `AuctionOrders.PayoutCompletedAt`.
  - `AddColumn: OrderStatusHistories.AuctionCallbackStatus`, `OrderStatusHistories.AuctionCallbackSentAt`.
