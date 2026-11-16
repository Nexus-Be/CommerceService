# 04 — MÔ HÌNH THỰC THỂ & CƠ SỞ DỮ LIỆU (COMMERCE SERVICE DATA PERSISTENCE)

> **Trạng thái triển khai (2026-11-16):** ✅ ĐÃ HOÀN THIỆN & CHUẨN HÓA — 16 Entities, 16 Configurations Fluent API, 4 Migrations EF Core, Soft Delete toàn phần, Optimistic Concurrency Token, và Seed Data hoạt động 100%. Bãi bỏ hoàn toàn các bảng Outbox/Inbox của MassTransit.
> **Nguồn sự thật:** Code trong `Commerce.Infrastructure/Persistence/` (`AppDbContext.cs`, `Configurations/`, `Migrations/`).
> **Cập nhật lần cuối:** 2026-11-16.

---

## 1. DANH MỤC 16 THỰC THỂ & BẢNG CƠ SỞ DỮ LIỆU

`AppDbContext` khai báo đủ **16 `DbSet<T>`** (`Commerce.Infrastructure/Persistence/AppDbContext.cs:14-29`), tương ứng với 16 bảng trong PostgreSQL `commerce_db`:

| # | Entity | `DbSet` | Bảng CSDL | Trách nhiệm chính |
|---|---|---|---|---|
| 1 | `Cart` | `Carts` | `Carts` | Quản lý giỏ hàng active của người mua, optimistic lock `RowVersion`. |
| 2 | `CartItem` | `CartItems` | `CartItems` | Dòng sản phẩm trong giỏ, snapshot đơn giá và tên sản phẩm. |
| 3 | `Order` | `Orders` | `Orders` | Đơn hàng tổng (Master Order), điều phối thanh toán, hạn thanh toán `PaymentDeadlineAt`. |
| 4 | `SubOrder` | `SubOrders` | `SubOrders` | Đơn con phân bổ theo từng Shop/Seller, quản lý trạng thái giao vận và thanh toán. |
| 5 | `SubOrderItem` | `SubOrderItems` | `SubOrderItems` | Chi tiết mặt hàng trong đơn con (SKU, số lượng, giá bán, thành tiền). |
| 6 | `DiscountCode` | `DiscountCodes` | `DiscountCodes` | Mã giảm giá voucher hệ thống hoặc voucher shop. |
| 7 | `DiscountCodeScope` | `DiscountCodeScopes` | `DiscountCodeScopes` | Phạm vi áp dụng của voucher (System / Shop / Category). |
| 8 | `DiscountCodeUsage` | `DiscountCodeUsages` | `DiscountCodeUsages` | Lịch sử sử dụng voucher của người mua. |
| 9 | `AuctionOrder` | `AuctionOrders` | `AuctionOrders` | Đơn hàng phát sinh từ phiên đấu giá thắng cuộc, quản lý cọc và thanh toán. |
| 10 | `OrderStatusHistory` | `OrderStatusHistories` | `OrderStatusHistories` | Nhật ký ghi nhận mọi lần chuyển đổi trạng thái của đơn hàng. |
| 11 | `OrderInvoice` | `OrderInvoices` | `OrderInvoices` | Hóa đơn điện tử gắn liền với đơn hàng. |
| 12 | `OrderCancellation` | `OrderCancellations` | `OrderCancellations` | Biên bản hủy đơn kèm lý do (`CancellationReasonCode`). |
| 13 | `OrderRefund` | `OrderRefunds` | `OrderRefunds` | Biên bản hoàn tiền cho đơn hàng con (`RefundReasonCode`). |
| 14 | `SystemSetting` | `SystemSettings` | `SystemSettings` | Tham số cấu hình động toàn hệ thống (phí sàn, thời hạn thanh toán...). |
| 15 | `DailyOrderSequence` | `DailyOrderSequences` | `DailyOrderSequences` | Bộ sinh số thứ tự mã đơn hàng hàng ngày. |
| 16 | `AuditLog` | `AuditLogs` | `AuditLogs` | Nhật ký kiểm toán bảo mật ghi lại các thao tác thay đổi trạng thái nhạy cảm. |

---

## 2. LỊCH SỬ MIGRATIONS CHÍNH THỨC

Toàn bộ schema được đồng bộ hóa qua 4 bản migration liên tiếp:
1. `20261031002403_InitialCreate.cs`: Khởi tạo toàn bộ 16 bảng, khóa ngoại, unique indices và quan hệ Clean Architecture.
2. `20261106065814_FixSubOrderJsonSeed.cs`: Khắc phục định dạng snapshot JSONB của địa chỉ giao hàng và seed dữ liệu mẫu.
3. `20261112150600_AddShipmentTrackingToSubOrder.cs`: Bổ sung trường `ShipmentTracking` kiểu JSONB vào `SubOrder` phục vụ theo dõi hành trình GHN thời gian thực.
4. `20261115160000_AddAuctionDepositAndSourceFields.cs`: Bổ sung trường tiền đặt cọc `DepositAmount`, trừ cọc `DepositDeductedAmount`, và phân loại nguồn đơn `AuctionOrderSource` vào `AuctionOrder`.

---

## 3. CÁC QUY TẮC BẢO TOÀN DỮ LIỆU (DATABASE INVARIANTS)

1. **Công thức Tài chính Bất biến (Financial Invariant)**:
   - `Order.TotalAmount = Sum(SubOrder.TotalAmount)`
   - Với mỗi `SubOrder`: `TotalAmount = ItemsSubtotal - DiscountAmount + ShippingFeeAmount`
   - `TotalAmount = SellerReceiveAmount + PlatformFeeAmount + ShippingFeeAmount` (PlatformFeeAmount có thể âm nếu sàn trợ giá voucher).
2. **Khóa Lạc Quan (Optimistic Concurrency Control)**:
   - Áp dụng trên các thực thể có tranh chấp ghi cao: `Cart`, `Order`, `SubOrder`, `AuctionOrder`, `OrderRefund`.
   - Cấu hình qua trường `RowVersion` (`long`) với `.IsConcurrencyToken()`.
3. **Xóa Mềm (Soft Delete & Global Query Filter)**:
   - Mọi thực thể kế thừa `ISoftDelete` (`IsDeleted`, `DeletedAt`).
   - Tự động áp dụng query filter toàn cục: `builder.HasQueryFilter(e => !e.IsDeleted)`.
4. **Lưu trữ Cấu trúc Phức hợp Dạng JSONB**:
   - `ShippingAddressSnapshot` trong `SubOrder` và `AuctionOrder` được ánh xạ qua EF Core 8 `.ToJson()`.
   - `ShipmentTrackingSnapshot` lưu thông tin mã vận đơn và mốc thời gian hành trình giao hàng.
