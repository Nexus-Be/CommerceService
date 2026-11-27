# BƯỚC 2: PHÂN TÍCH CHI TIẾT CƠ CHẾ KỸ THUẬT VÀ NGHIỆP VỤ COMMERCE SERVICE

> **Ngày thực hiện:** 2026-11-27  
> **Tài liệu tạo lập:** `docs/analysis/STEP2_DETAILED_ANALYSIS.md`  

---

## 1. CƠ CHẾ BẢO MẬT, TIẾP NHẬN IDENTITY & PHÂN QUYỀN (RBAC)

### 1.1. Cách CommerceService Nhận Diện Người Dùng (Identity Ingestion)
CommerceService đứng sau API Gateway / Reverse Proxy. API Gateway thực hiện xác thực JWT và chuyển tiếp thông tin định danh qua các HTTP Headers:
- **`x-user-id`**: Định dạng UUID (Guid) của người dùng hiện tại (hoặc qua JWT Claim `NameIdentifier` / `sub` / `id`).
- **`x-user-roles`**: Chuỗi các vai trò phân cách bằng dấu phẩy (ví dụ: `buyer`, `seller`, `admin`, `staff`).
- **`x-user-email`**: Địa chỉ email người dùng (tùy chọn).
- **`x-user-permissions`**: Danh sách quyền hạn (phân cách bằng dấu phẩy).
- **`Idempotency-Key`**: Chuỗi định danh duy nhất của request dùng cho các thao tác thanh toán / tạo đơn hàng nhằm chống gửi trùng (click đúp).

### 1.2. Cơ chế Bảo Vệ (Filters & Guards)
1. **`[RequireRole("role1", "role2")]`**:
   - Chặn ngay tại tầng Action/Controller nếu không có `x-user-id` hợp lệ -> Trả về HTTP `401 Unauthorized`.
   - Kiểm tra nếu `roles` chứa vai trò được yêu cầu, hoặc là `admin`/`staff` -> Cho phép đi qua. Nếu không thỏa mãn -> Trả về HTTP `403 Forbidden`.
   - Cho phép ghi đè bằng `[AllowAnonymous]` cho các endpoint S2S hoặc Webhook.
2. **`EnsureOwnership(targetOwnerId)`**:
   - So khớp `currentUserId == targetOwnerId`.
   - Nếu không khớp và người dùng không phải là `admin` hoặc `staff` -> Lập tức trả về HTTP `403 Forbidden` (`FORBIDDEN`). Cấm tin tưởng tham số query `buyerId`.
3. **`InternalServiceKeyGuard`**:
   - Áp dụng cho các Webhook hoặc giao tiếp S2S nhạy cảm: `POST /api/v1/sub-orders/{subOrderId}/delivery-status`, `POST /api/v1/auction-orders/settlement`, `POST /api/v1/internal/...`.
   - So khớp header `X-Internal-Service-Key` với cấu hình môi trường bằng thuật toán so sánh chuỗi đẳng thời `CryptographicOperations.FixedTimeEquals` chống tấn công Timing Attack.

---

## 2. QUY CHUẨN ĐỊNH DẠNG DỮ LIỆU & XỬ LÝ LỖI (RFC 9110)

### 2.1. Chuẩn Hóa Response Lỗi (Problem Details)
Mọi lỗi nghiệp vụ hoặc kỹ thuật đều được bắt qua `GlobalExceptionHandlerMiddleware` hoặc `ApiValidationProblemFactory`, tuần tự hóa theo định dạng RFC 9110 (`application/problem+json`):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Business rule violation",
  "status": 400,
  "errorCode": "INSUFFICIENT_STOCK",
  "detail": "Requested quantity exceeds available warehouse stock.",
  "reasonCode": null,
  "traceId": "00-845187e5b306b3bc4f3583cbfca5cb3a-a1b2c3d4e5f60718-01",
  "timestamp": "2026-11-27T04:22:49Z"
}
```

Đối với lỗi Validation đầu vào (`VAL_INVALID_INPUT`):
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errorCode": "VAL_INVALID_INPUT",
  "detail": "Quantity must be greater than zero.",
  "errors": {
    "Quantity": ["Quantity must be greater than zero."]
  },
  "traceId": "00-...",
  "timestamp": "2026-11-27T04:22:49Z"
}
```

### 2.2. Ma Trận Mã Lỗi Hệ Thống Thường Gặp

| HTTP Status | ErrorCode | Ý nghĩa / Nguyên nhân | Hướng xử lý cho Frontend |
|---|---|---|---|
| `400` | `VAL_INVALID_INPUT` | Tham số gửi lên không đúng định dạng / thiếu trường bắt buộc | Hiển thị thông báo lỗi trường nhập tương ứng |
| `400` | `INSUFFICIENT_STOCK` | Kho không đủ số lượng hàng tồn khả dụng | Thông báo hết hàng, cập nhật lại số lượng tối đa |
| `400` | `CATALOG_SKU_INACTIVE` | Sản phẩm đã bị ngừng bán hoặc ẩn khỏi sàn | Thông báo sản phẩm không khả dụng, mời chọn món khác |
| `400` | `CATALOG_VARIANT_MISMATCH` | Biến thể SKU mới không thuộc cùng sản phẩm cha | Chặn chọn nhầm biến thể |
| `400` | `QUOTE_EXPIRED` | Phiên báo giá đã hết hạn (TTL 15m) hoặc đã dùng | Gọi lại API `/quote` để lấy `quoteId` mới |
| `400` | `PAYMENT_DEADLINE_EXPIRED` | Đã quá hạn 15 phút thanh toán đơn hàng | Chuyển trạng thái đơn sang Hết hạn (Expired) |
| `400` | `INSUFFICIENT_WALLET_BALANCE` | Số dư ví Nexus của người mua không đủ | Mở popup nạp tiền ví hoặc chuyển phương thức COD |
| `401` | `UNAUTHORIZED` | Thiếu token hoặc thông tin `x-user-id` | Điều hướng người dùng về màn hình Đăng nhập |
| `403` | `FORBIDDEN` | Không có quyền truy cập hoặc không phải chủ đơn hàng | Thông báo từ chối truy cập |
| `404` | `NOT_FOUND` / `ORDER_NOT_FOUND` / `CART_NOT_FOUND` | Bản ghi không tồn tại hoặc đã bị xóa mềm | Thông báo dữ liệu không tồn tại |
| `409` | `PRICE_CHANGED` | Phát hiện tăng giá sản phẩm khi bấm đặt hàng | Hiển thị popup biến động giá, cập nhật giỏ |
| `409` | `SHIPPING_FEE_MISMATCH` | Phí ship tại client không khớp với phí server đã báo | Gọi lại API tính lại phí ship để đồng bộ |
| `409` | `VOUCHER_NO_LONGER_VALID` | Voucher hết lượt hoặc không còn thỏa điều kiện | Bỏ chọn voucher và báo người mua tính lại |
| `409` | `CONCURRENCY_CONFLICT` | Tranh chấp ghi đồng thời (Optimistic Concurrency) | Thông báo dữ liệu vừa bị thay đổi, mời thử lại |
| `422` | `SKU_WEIGHT_UNAVAILABLE` | SKU thiếu thông số cân nặng/kích thước để tính phí GHN | Báo lỗi cấu hình sản phẩm phía người bán |
| `500` | `ORDER_CREATION_FAILED` | Lỗi lưu DB sau khi trừ tiền ví; đã bồi hoàn 100% | Báo lỗi hệ thống, xác nhận tiền ví đã an toàn |

---

## 3. CÔNG THỨC TÀI CHÍNH & BẤT BIẾN MIỀN (FINANCIAL INVARIANTS)

Mọi đơn hàng trong CommerceService đều tuân thủ các công thức tài chính bất biến:
1. **Đơn hàng cha (Master Order):**
   $$\text{Order.TotalAmount} = \sum \text{SubOrder.TotalAmount}$$
2. **Đơn hàng con (SubOrder):**
   $$\text{SubOrder.TotalAmount} = \text{ItemsAmount} - \text{DiscountAmount} + \text{ShippingFeeAmount}$$
   $$\text{DiscountAmount} = \text{ProductDiscountAmount} + \text{ShopDiscountAmount} + \text{SystemDiscountAmount}$$
3. **Phân bổ Doanh thu & Phí sàn:**
   $$\text{SubOrder.TotalAmount} = \text{SellerReceiveAmount} + \text{PlatformFeeAmount} + \text{ShippingFeeAmount}$$
   - `SellerReceiveAmount`: Số tiền người bán thực nhận được giải ngân vào ví.
   - `PlatformFeeAmount`: Phí hoa hồng sàn thu được (có thể âm nếu sàn chi voucher trợ cấp vượt quá hoa hồng).
   - `ShippingFeeAmount`: Tiền phí vận chuyển trả cho đối tác giao hàng (GHN).

---

## 4. QUY TRÌNH QUẢN LÝ VÒNG ĐỜI TRẠNG THÁI (STATE MACHINES)

### 4.1. Vòng Đời Đơn Hàng Con Thường (`SubOrderStatus`)
```
PendingPayment (0)
     │
     ├──(Thanh toán ví thành công / Đơn COD)──▶ Paid (1)
     │                                           │
     │                                           ├──(Seller bấm "Accept")──▶ Confirmed (2) [Tạo vận đơn GHN]
     │                                           │                                │
     │                                           ├──(Seller bấm "Reject")──▶ Cancelled (6) [Hoàn tiền ví 100%]
     │                                           │                                │
     │                                           │                                ├──(GHN lấy hàng)──▶ Shipped (3)
     │                                           │                                                        │
     │                                           │                                                        ├──(GHN giao thành công)──▶ Delivered (4)
     │                                           │                                                        │                              │
     │                                           │                                                        │                              ├──(Buyer xác nhận)──▶ Completed (5) [Giải ngân Escrow]
     │                                           │                                                        │                              │
     │                                           │                                                        │                              └──(Yêu cầu hoàn tiền)──▶ Refunded (7)
     │                                           │                                                        │
     │                                           │                                                        └──(Giao thất bại / Bom hàng)──▶ Returning (8) ──▶ Cancelled (6)
     └──(Quá hạn 15m / Hủy đơn)──────────────────▶ Cancelled (6)
```

### 4.2. Vòng Đời Đơn Hàng Đấu Giá (`AuctionOrder`)
1. **Khởi tạo (`PendingPayment`):** Nhận settlement từ AuctionService, cọc được ghi nhận `DepositAmount`.
2. **Chốt Ship:** Winner gửi địa chỉ GHN -> tính `ShippingFeeAmount`, cập nhật `TotalAmount`.
3. **Thanh toán (`Paid`):** Winner thanh toán số tiền còn lại (`RemainingAmount = TotalAmount - DepositAmount`). Kích hoạt bộ đếm thời gian 24 giờ cho Seller.
4. **Tiếp nhận / Từ chối:**
   - Seller Accept -> `Confirmed`, tự động sinh mã vận đơn GHN.
   - Seller Reject (hoặc quá 24h) -> `Cancelled`, tự động hoàn trả 100% (Tiền thanh toán + Tiền cọc).
5. **Giao hàng & Hoàn tất:** GHN giao thành công -> Buyer bấm xác nhận nhận hàng -> `Completed`, giải ngân tiền hàng sang ví Seller.

---

## 5. BỐ TRÍ DỮ LIỆU & QUAN HỆ THỰC THỂ CÓ ẢNH HƯỞNG TỚI FRONTEND

```
[ Cart ] 1 ─── * [ CartItem ] (Snapshot: SkuId, ProductId, UnitPriceSnapshot, Quantity)
   │
   ▼ (Tạo Quote & Order)
[ Order ] (Master Order: TotalAmount, PaymentDeadlineAt, Status)
   │
   ├── 1 ─── * [ SubOrder ] (Phân bổ theo SellerId, ShopName, ShippingFee, Status, Escrow)
   │               │
   │               ├── 1 ─── * [ SubOrderItem ] (Line items: SkuId, UnitPrice, Quantity, LineTotal)
   │               ├── 1 ─── 1 [ ShippingAddressSnapshot ] (JSONB snapshot)
   │               ├── 1 ─── 1 [ ShipmentTrackingSnapshot ] (JSONB snapshot: TrackingCode, Events)
   │               └── 1 ─── * [ OrderRefund ] (Lịch sử hoàn tiền: Status, Amount, Reason)
   │
   └── 1 ─── * [ DiscountCodeUsage ] (Lịch sử voucher đã áp dụng)

[ AuctionOrder ] (Đơn lẻ độc lập: WinningAmount, DepositAmount, ShippingFee, Status, GHN Tracking)
```

Tất cả bảng đều áp dụng **Xóa mềm (Soft Delete - `IsDeleted`)** và **Khóa lạc quan (Optimistic Concurrency - `RowVersion`)**. Khi FE cập nhật trên một bản ghi cũ bị tranh chấp, hệ thống sẽ trả về lỗi `409 CONCURRENCY_CONFLICT`.

---

## 6. CÁC TÁC VỤ NỀN TỰ ĐỘNG (BACKGROUND WORKERS) & ẢNH HƯỞNG ĐẾN TRẠNG THÁI

1. **`PaymentTimeoutWorker` (Chu kỳ quét liên tục):**
   - Quét các đơn hàng `PendingPayment` có `PaymentDeadlineAt < UtcNow`.
   - Tự động chuyển `OrderStatus` và `SubOrderStatus` thành `Cancelled` (hoặc `Expired`).
   - Gọi `FulfillmentService.ReleaseStockAsync` nhả tồn kho đã giữ.
   - Gọi `VoucherLifecycleService.ReleaseReservationsAsync` hoàn lại lượt voucher.
   - *Hệ quả cho FE:* Sau 15 phút kể từ khi tạo đơn mà không thanh toán, màn hình chi tiết đơn hàng sẽ tự động chuyển sang trạng thái đã hủy do hết hạn thanh toán.
2. **`AuctionFinancialRetryWorker`:**
   - Quét các đơn hàng đấu giá gặp lỗi hoàn tiền hoặc lỗi thanh toán tạm thời để thực hiện lại, đảm bảo tiền ví của người dùng luôn được hoàn trả đúng hạn.
3. **`SettlementRetryWorker`:**
   - Thử lại các giao dịch giải ngân Escrow cho Người bán khi WalletService bị gián đoạn mạng.
4. **`NotificationOutboxWorker`:**
   - Đọc bảng outbox thông báo thanh toán đơn hàng thành công và gửi sang dịch vụ Notification để bắn Push/Email cho khách hàng và người bán.
