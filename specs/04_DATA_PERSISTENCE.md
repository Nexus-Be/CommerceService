# 04 — MÔ HÌNH THỰC THỂ & CƠ SỞ DỮ LIỆU (COMMERCE SERVICE DATA PERSISTENCE - V2)
> **Trạng thái triển khai (2026-10-24):** ✅ Phần lớn — 16/16 entity có `DbSet` + 16/16 configuration + 1 migration + seed; ⚠️ tầng ghi chưa hoạt động (`SaveChangesAsync` 0 lời gọi) và 3 enum có alias trùng giá trị.
> **Nguồn sự thật:** code trong repo này (`file:line` ghi kèm trong từng bảng).
> **Cập nhật lần cuối:** 2026-10-24.
---
## 0. ĐỐI CHIẾU NHANH: 16 ENTITY ↔ 16 `DbSet` ↔ 16 BẢNG `commerce.dbml`
`AppDbContext` khai báo đúng **16 `DbSet`** (`Commerce.Infrastructure/Persistence/AppDbContext.cs:14-29`), trùng **16 entity**, và trùng **16 `Table`** trong `dbml/service/commerce.dbml` của workspace root.
| # | Entity | `DbSet` | Bảng dbml | Trạng thái | Bằng chứng |
|---|---|---|---|---|---|
| 1 | `Cart` | `Carts` | `Carts` | ✅ CÓ | `AppDbContext.cs:14` |
| 2 | `CartItem` | `CartItems` | `CartItems` | ✅ CÓ | `AppDbContext.cs:15` |
| 3 | `Order` | `Orders` | `Orders` | ✅ CÓ | `AppDbContext.cs:16` |
| 4 | `SubOrder` | `SubOrders` | `SubOrders` | ✅ CÓ | `AppDbContext.cs:17` |
| 5 | `SubOrderItem` | `SubOrderItems` | `SubOrderItems` | ✅ CÓ | `AppDbContext.cs:18` |
| 6 | `DiscountCode` | `DiscountCodes` | `DiscountCodes` | ✅ CÓ | `AppDbContext.cs:19` |
| 7 | `DiscountCodeScope` | `DiscountCodeScopes` | `DiscountCodeScopes` | ✅ CÓ | `AppDbContext.cs:20` |
| 8 | `DiscountCodeUsage` | `DiscountCodeUsages` | `DiscountCodeUsages` | ✅ CÓ | `AppDbContext.cs:21` |
| 9 | `AuctionOrder` | `AuctionOrders` | `AuctionOrders` | ✅ CÓ | `AppDbContext.cs:22` |
| 10 | `OrderStatusHistory` | `OrderStatusHistories` | `OrderStatusHistories` | ✅ CÓ | `AppDbContext.cs:23` |
| 11 | `OrderInvoice` | `OrderInvoices` | `OrderInvoices` | ✅ CÓ | `AppDbContext.cs:24` |
| 12 | `OrderCancellation` | `OrderCancellations` | `OrderCancellations` | ✅ CÓ | `AppDbContext.cs:25` |
| 13 | `OrderRefund` | `OrderRefunds` | `OrderRefunds` | ✅ CÓ | `AppDbContext.cs:26` |
| 14 | `SystemSetting` | `SystemSettings` | `SystemSettings` | ✅ CÓ | `AppDbContext.cs:27` |
| 15 | `DailyOrderSequence` | `DailyOrderSequences` | `DailyOrderSequences` | ✅ CÓ | `AppDbContext.cs:28` |
| 16 | `AuditLog` | `AuditLogs` | `AuditLogs` | ✅ CÓ | `AppDbContext.cs:29` |
| — | `OrderSagaState` (đặc tả cũ) | — | — | ❌ CHƯA CÓ | tên này không tồn tại trong repo |
Hạ tầng schema khác:
- 1 migration duy nhất: `Commerce.Infrastructure/Migrations/20261024103145_InitialCommerceSchema.cs`.
- Seed data: `Commerce.Infrastructure/Persistence/Seeds/CommerceSeedData.cs` (`SeedDiscountCodes`, `SeedSystemSettings`, `SeedCarts`, `SeedOrders`), gọi từ `AppDbContext.cs:35`.
- 3 bảng Outbox/Inbox do MassTransit sinh: `AppDbContext.cs:38-40`.
---
## 1. MÔ HÌNH THỰC THỂ CHÍNH (DOMAIN ENTITIES)
Trạng thái của khối này: ✅ **entity/property đã có** (đọc/ghi schema được), nhưng ⚠️ **chưa có nghiệp vụ** nào thao tác lên chúng (0 lệnh ghi, 0 hàm chuyển trạng thái).
1. **`Cart` (Giỏ hàng)** ✅ CÓ — `Commerce.Domain/Entities/Cart.cs:6-18`:
   - `Id`: `Guid` (kế thừa `BaseEntity<Guid>.Id`, `Commerce.Domain/Base/BaseEntity.cs:5`).
   - `BuyerId`: `Guid` (Liên kết logic `UserService.Users.Id`).
   - `Status`: `CartStatus` (`Active = 0`, `CheckedOut = 1`, `Abandoned = 2`) — `Commerce.Domain/Enums/CartStatus.cs:5-7`.
   - `ItemCount`: `int`.
   - `SubtotalAmount`: `decimal` (precision 18,2 — `CartConfiguration.cs:13`).
   - `LastActivityAt`: `DateTimeOffset` (khởi tạo `DateTimeOffset.UtcNow` — `Cart.cs:12`).
   - `RowVersion`: `long` (Optimistic locking: `.IsConcurrencyToken()` — `CartConfiguration.cs:14`).
   - Navigation: `ICollection<CartItem> Items`, `ICollection<Order> Orders`.
2. **`CartItem`** ✅ CÓ — `Commerce.Domain/Entities/CartItem.cs:5-18`:
   - `Id`: `Guid`; `CartId`: `Guid` (FK → `Cart.Id`); `ProductId`: `Guid` (Cross-service Ref → `CatalogService`); `SkuId`: `Guid` (bắt buộc, `required`); `SellerId`: `Guid` (Cross-service Ref → `UserService`).
   - `Quantity`: `int` (⚠️ không có ràng buộc `Quantity > 0` trong configuration/entity — chỉ là quy ước).
   - `UnitPriceSnapshot`: `decimal`; `ProductNameSnapshot`: `string`; `AddedAt`: `DateTimeOffset` (`CartItem.cs:14`).
   - Navigation: `Cart? Cart`.
   - Ràng buộc DB thật: unique index `(CartId, SkuId)`, index `ProductId` (`CartItemConfiguration.cs:15-16`).
3. **`Order` (Đơn hàng tổng / Master Order - Trước đây là CheckoutGroup)** ✅ CÓ — `Commerce.Domain/Entities/Order.cs:6-24`:
   - `Id`: `Guid`; `CartId`: `Guid?` (FK → `Cart.Id`, `OnDelete` = `SetNull` — `OrderConfiguration.cs:28`); `BuyerId`: `Guid`.
   - `Status`: `OrderStatus` (`PendingPayment = 0`, `PaymentProcessing = 1`, `Paid = 2`, `Cancelled = 3`, `Expired = 4`) — `Commerce.Domain/Enums/OrderStatus.cs:5-9`.
   - `TotalAmount`: `decimal` (Precision 18,2); `WalletTransactionId`: `Guid?`.
   - `PaymentAttemptCount`: `int` (đặc tả nói tối đa 3 lần — ⚠️ chưa có hằng số/logic giới hạn); `PaymentDeadlineAt`: `DateTimeOffset`.
   - `RowVersion`: `long` (`OrderConfiguration.cs:14`).
   - Navigation: `Cart?`, `ICollection<SubOrder> SubOrders`, `ICollection<DiscountCodeUsage> DiscountCodeUsages`, `OrderInvoice? Invoice`, `ICollection<OrderRefund> Refunds`.
4. **`SubOrder` (Đơn hàng con theo từng Shop/Seller - Trước đây là Order)** ✅ CÓ — `Commerce.Domain/Entities/SubOrder.cs:6-44`:
   - `SubOrderNo`: `string` (Unique Index, định dạng `ORD-YYYYMMDD-NNNNNN` — format chỉ là quy ước, chưa có hàm sinh); `OrderId`: `Guid` (FK → `Order.Id`, `OnDelete` = `Restrict` — `OrderConfiguration.cs:23`); `BuyerId`, `SellerId`: `Guid`.
   - `Status`: `SubOrderStatus` — ⚠️ **enum có alias trùng giá trị**: `PendingPayment = 0`, `Paid = 1`, `Confirmed = 2` **và** `SellerAccepted = 2`, `Shipping = 3` **và** `Shipped = 3`, `Delivered = 4`, `Completed = 5`, `Cancelled = 6`, `Refunded = 7` (`Commerce.Domain/Enums/SubOrderStatus.cs:5-14`).
   - `PaymentMethod`: `PaymentMethod` (`Wallet = 0`, `VnPay = 1`, `Cod = 2`).
   - `WalletTransactionId`: `Guid?`; `ShippingAddress`, `ContactPhone`: `string`.
   - `ItemsAmount`, `ProductDiscountAmount`, `ShopDiscountAmount`, `PlatformMarkupAmount`, `SystemDiscountAmount`, `ShippingFeeAmount`, `TotalAmount`, `SellerReceiveAmount`, `PlatformFeeAmount`: `decimal`.
   - Invariant tổng tiền: `TotalAmount = SellerReceiveAmount + PlatformFeeAmount + ShippingFeeAmount` — ⚠️ chỉ là đặc tả, **không** được enforce trong code.
   - `PaymentDeadlineAt` (bắt buộc), `SellerRespondDeadlineAt`, `PaidAt`, `SellerAcceptedAt`, `ShippedAt`, `DeliveredAt`, `CompletedAt`, `CancelledAt`: `DateTimeOffset?`.
   - `RowVersion`: `long` (`SubOrderConfiguration.cs:27`).
5. **`SubOrderItem` (Dòng sản phẩm đơn con - Trước đây là OrderItem)** ✅ CÓ — `Commerce.Domain/Entities/SubOrderItem.cs:6-...`:
   - `SubOrderId`: `Guid` (FK → `SubOrder.Id`); `ProductId`, `SkuId`, `SellerId`: `Guid`.
   - `ProductNameSnapshot`, `SkuCodeSnapshot`, `SkuAttributesSnapshot` (jsonb), `ImageUrlSnapshot`: snapshot tại thời điểm đặt hàng (`SubOrderItemConfiguration.cs:12-15`).
   - `Quantity`: `int`; `UnitPrice`, `UnitPriceAfterDiscount`, `LineTotal`: `decimal` (18,2); `DiscountCodeId`: `Guid?`.
   - ⚠️ Đặc tả cũ gọi entity này là `OrderLine` — tên đó **không tồn tại**.
6. **`AuctionOrder` (Đơn hàng từ kết quả đấu giá)** ✅ CÓ (schema) / ⚠️ không có nguồn ghi:
   - `OrderNo` (unique), `AuctionId`, `BuyerId`, `SellerId`, `WinningBidId`, `ProductId`, `SkuId?`, snapshot sản phẩm, các cột tiền `decimal`, `Status` (`SubOrderStatus`), `PaymentMethod`, `WalletTransactionId?`, `RowVersion` (`.IsConcurrencyToken()` — `AuctionOrderConfiguration.cs:26`).
   - ⚠️ Chưa có `IAuctionEndedEvent`/consumer nào ghi vào bảng này (xem `03_EVENT_MESSAGING.md`).
7. **`OrderRefund` (Yêu cầu hoàn tiền - bổ sung ở v2)** ✅ CÓ — `Commerce.Domain/Entities/OrderRefund.cs`:
   - `SubOrderId?`, `AuctionOrderId?`, `OrderId?`, `CancellationId?`; `RefundType` (`Full = 0`, `Partial = 1`); `Amount` (18,2); `Status` (`RefundStatus`: `Requested = 0`, `Completed = 1`, `Failed = 2`); `ReasonCode`, `ReasonNote?`; `ActorRole` (`OrderActorRole`); `RequestedBy?`; `WalletTransactionId?`, `RefundTransactionId?`, `RequestEventId?`, `FailureCode?`; `RequestedAt`, `CompletedAt?`, `FailedAt?`; `RowVersion` (`OrderRefundConfiguration.cs:19`).
8. **`DiscountCode`, `DiscountCodeScope`, `DiscountCodeUsage`** ✅ CÓ:
   - `DiscountCode`: `IsVisible = true` (thay cho `IsDisabled`); `ScopeType`: `DiscountScopeType` — ⚠️ **enum alias**: `System = 0` **và** `Global = 0`, `Shop = 1`, `Product = 2` (`Commerce.Domain/Enums/DiscountScopeType.cs:5-8`); `DiscountType` — ⚠️ **enum alias**: `Percent = 0`/`Percentage = 0`, `Fixed = 1`/`FixedAmount = 1` (`Commerce.Domain/Enums/DiscountType.cs:5-8`). Cả hai cột lưu **chuỗi tên** qua `HasConversion<string>()` (`DiscountCodeConfiguration.cs:15-16`) nên alias gây lệch giá trị so sánh.
   - `DiscountCodeScope`: `ScopeRefId` nullable (cho phép `All`); `ScopeRefType` = `Shop|Product|Sku|Category|All` (`Commerce.Domain/Enums/DiscountScopeRefType.cs:5-9`), lưu chuỗi (`DiscountCodeScopeConfiguration.cs:12`).
   - `DiscountCodeUsage`: lưu vết giữ chỗ (`Reserved`), áp dụng (`Used`), giải phóng (`Released`) qua `DiscountUsageStatus`, lưu chuỗi (`DiscountCodeUsageConfiguration.cs:12`); `ReservedAt` khởi tạo `DateTimeOffset.UtcNow` (`DiscountCodeUsage.cs:14`).
9. **`OrderStatusHistory`, `OrderInvoice`, `OrderCancellation`** ✅ CÓ:
   - `OrderStatusHistory`: `SubOrderId?`/`AuctionOrderId?`, `FromStatus?`/`ToStatus` (`SubOrderStatus`, lưu chuỗi), `ChangedBy?`, `ActorRole` (hỗ trợ `SupportStaff`), `Reason?` (`OrderStatusHistoryConfiguration.cs:12-14`).
   - `OrderInvoice`: `Status` (`InvoiceStatus`: `Generated = 0`, `Failed = 1`), `InvoiceNo` unique, `IssuedAt` khởi tạo `DateTimeOffset.UtcNow` (`OrderInvoice.cs:15`).
   - `OrderCancellation`: `ActorRole`, `ReasonCode` (`CancellationReasonCode`), `ReasonNote?`, `WasPaid`, `RefundTransactionId?`, `InventoryReleased`, navigation `Refunds` (`OrderCancellation.cs:6-21`).
10. **`SystemSetting` (Cấu hình hệ thống động)** ✅ CÓ (bảng + seed) / ⚠️ chưa có service đọc:
    - Các khóa timeout/hạn phản hồi/retry/số lượng: `CART_EXPIRATION_DAYS`, `CHECKOUT_TIMEOUT_MINUTES`, `SELLER_RESPOND_HOURS`, `ORDER_AUTO_COMPLETE_DAYS`, `PAYMENT_RETRY_LIMIT`, `MAX_QTY_PER_ITEM`, `MAX_CART_ITEMS` — seed tại `CommerceSeedData.cs`; `SettingKey` unique (`SystemSettingConfiguration.cs:13`).
11. **`DailyOrderSequence` (Chuỗi sinh số thứ tự đơn theo ngày)** ✅ CÓ:
    - Khóa chính kết hợp `(SeqDate, SeqType)` (`DailyOrderSequenceConfiguration.cs`); `SeqType` = `OrderNo=0|AuctionOrderNo=1|InvoiceNo=2` (`Commerce.Domain/Enums/SequenceType.cs`).
    - ⚠️ Chưa có cơ chế an toàn khi concurrent (không có `UPDATE ... RETURNING`, không dùng PG sequence) — xem CO-04.
12. **`AuditLog`** ✅ CÓ:
    - Nhật ký kiểm toán; `Action` (`AuditAction`) lưu chuỗi (`AuditLogConfiguration.cs:16`), `CreatedAt` khởi tạo `DateTimeOffset.UtcNow` (`AuditLog.cs:20`).
---
## 2. QUY ƯỚC KỸ THUẬT ĐANG ÁP DỤNG
1. **Enum lưu dạng chuỗi**: 24 chỗ `HasConversion<string>()` trên 16 configuration (ví dụ `CartConfiguration.cs:12`, `OrderConfiguration.cs:12`, `SubOrderConfiguration.cs:14-15`, `DiscountCodeConfiguration.cs:15-16,20`). Đây là lý do alias enum ở mục 1.4/1.8 là **bug thật**, không chỉ là vấn đề thẩm mỹ.
2. **Concurrency**: `RowVersion` là `long` + `.IsConcurrencyToken()` trên 5 entity (`Cart`, `Order`, `SubOrder`, `OrderRefund`, `AuctionOrder`) — không dùng `[Timestamp]`/`xmin` của PostgreSQL.
3. **Soft delete**: `BaseEntity<TKey>` có `IsDeleted`/`DeletedAt` (`Commerce.Domain/Base/BaseEntity.cs:8-9`); mọi configuration gắn `HasQueryFilter(x => !x.IsDeleted)`.
4. **Thời gian**: ⚠️ **10 chỗ** khởi tạo `DateTimeOffset.UtcNow` ngay trong field initializer, **không** có `IClock`/`TimeProvider` (grep = 0): `BaseEntity.cs:6`, `BaseEntity.cs:7`, `Cart.cs:12`, `CartItem.cs:14`, `DiscountCodeUsage.cs:14`, `AuditLog.cs:20`, `OrderRefund.cs:23`, `OrderInvoice.cs:15`, `DailyOrderSequence.cs:11`, `DailyOrderSequence.cs:12`. Hệ quả: không thể freeze/giả lập thời gian trong test và seed phải gán đè thủ công (ví dụ `CommerceSeedData.cs:20-21`).
5. **Migration**: `Program.cs:42-56` tự chạy `db.Database.Migrate()` lúc khởi động (bọc `try/catch`, chỉ khi `IsRelational()`).