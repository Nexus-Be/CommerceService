# COMMERCE SERVICE — TÀI LIỆU TÍCH HỢP LUỒNG NGHIỆP VỤ CHO FRONTEND (FE)

> **Dành cho:** Frontend Engineers (Web Buyer, Mobile App, Seller Portal, Admin Portal)  
> **Service:** CommerceService (Port `8003` - Bounded Context: Giỏ hàng, Đơn hàng đa người bán, Đấu giá & Escrow Settlement)  
> **Kiến trúc:** 100% RESTful Web API (RFC 9110 Problem Details). Không dùng gRPC, không dùng RabbitMQ/MassTransit.  
> **Ngôn ngữ tài liệu:** Tiếng Việt (giữ nguyên thuật ngữ kỹ thuật, tên API và tên trường dữ liệu).  
> **Cập nhật lần cuối:** 2026-11-27  

---

## 1. TỔNG QUAN HỆ THỐNG (SYSTEM OVERVIEW)

### 1.1. Trách nhiệm của CommerceService
**CommerceService** là hạt nhân điều phối giao dịch thương mại của nền tảng Marketplace đa người bán. Dịch vụ chịu trách nhiệm độc quyền đối với:
- **Quản lý Giỏ hàng (Cart):** Giỏ hàng cá nhân, thêm/sửa/xóa, đổi phân loại biến thể SKU trực tiếp, checkbox chọn món tính tiền (Shopee selection), gộp giỏ vãng lai khi đăng nhập.
- **Báo giá & Đặt hàng Đa Người Bán (Quote & Two-Phase Order Placement):** Tiếp nhận yêu cầu báo giá xem trước (TTL 15m), tính cước GHN riêng cho từng Shop, thẩm định và xếp chồng voucher, khóa kho 15 phút, thanh toán ví Nexus hoặc COD, dọn giỏ sau checkout.
- **Vòng đời Đơn hàng & Quản lý Giao vận (Order & Shipment Lifecycle):** Phân rã cấu trúc Đơn hàng cha (`Order`) và Đơn hàng con (`SubOrder`), điều phối Người bán tiếp nhận đơn, tạo vận đơn GHN, webhook cập nhật tiến độ giao vận, người mua xác nhận nhận hàng và tự động giải ngân Escrow sang ví Shop.
- **Đơn hàng Đấu giá (Auction Orders):** Tiếp nhận kết quả trúng đấu giá từ AuctionService, chốt địa chỉ và cước GHN, thanh toán trừ tiền cọc, người bán xác nhận trong 24 giờ, giải ngân Escrow hoặc hoàn tiền 100% khi có sự cố.
- **Khuyến mãi & Mã Giảm Giá (Discount Engine):** Áp dụng voucher đa tầng (Sản phẩm -> Gian hàng -> Toàn sàn), thuật toán gợi ý combo voucher tối ưu.
- **Hoàn tiền & Tranh chấp (Refunds & Dispute):** Yêu cầu hoàn tiền đơn con, Người bán/Admin duyệt hoặc từ chối, giải ngân quỹ bảo hiểm bồi hoàn sàn có cơ chế duyệt 2 cấp.

### 1.2. Những việc CommerceService KHÔNG làm
- **Không quản lý tồn kho vật lý:** Việc kiểm tra và giữ kho do `FulfillmentService` thụ lý.
- **Không quản lý số dư tiền hay cổng thanh toán trực tiếp:** Việc trừ/hoàn tiền ví do `WalletService` thụ lý.
- **Không trực tiếp gọi sang đối tác vận chuyển bên thứ 3 (GHN):** Do `FulfillmentService` đảm nhiệm.
- **Không quản lý tài khoản người dùng, phân quyền hay đánh giá:** Do `UserService` đảm nhiệm.

### 1.3. Các Vai Trò Người Dùng (User Roles)
- **`buyer` (Người mua):** Khách hàng sử dụng Web hoặc Mobile App để duyệt giỏ hàng, đặt hàng, thanh toán ví/COD, theo dõi vận đơn, xác nhận đã nhận hàng, yêu cầu hoàn tiền.
- **`seller` (Người bán):** Chủ gian hàng sử dụng Seller Portal để quản lý danh sách đơn con của Shop, duyệt nhận đơn (`Accept` -> sinh mã GHN), từ chối đơn (`Reject` -> hoàn tiền khách), theo dõi doanh thu thực nhận (`Financial Breakdown`), tạo voucher khuyến mãi cho Shop.
- **`staff` (Nhân viên vận hành):** Tra cứu đơn hàng toàn sàn, tra cứu danh sách khiếu nại, xem % phí sàn, lập đề xuất trích quỹ bồi hoàn sàn.
- **`admin` (Quản trị viên sàn):** Toàn quyền hệ thống, thay đổi % phí sàn hoa hồng, thu hồi điểm phạt boom hàng cho người mua, phê duyệt các ca bồi hoàn quỹ bảo hiểm sàn.

### 1.4. Bảng Thuật Ngữ (Glossary)

| Thuật ngữ | Ý nghĩa kỹ thuật & nghiệp vụ |
|---|---|
| **Master Order (Order)** | Đơn hàng tổng đại diện cho toàn bộ giỏ hàng người mua thanh toán trong một lượt. |
| **SubOrder** | Đơn hàng con được tách ra từ Master Order, trực thuộc duy nhất 1 Người bán (Shop/Seller). |
| **Quote (Báo giá)** | Phiên tính trước tiền hàng, tiền ship GHN từng shop và chiết khấu voucher, có hạn sử dụng 15 phút (`TTL = 15m`). |
| **Escrow (Quỹ ủy thác)** | Cơ chế giữ tiền thanh toán của người mua trong ví tạm; chỉ giải ngân sang ví người bán khi người mua bấm "Đã nhận hàng" hoặc đơn giao thành công. |
| **Shopee Selection** | Cơ chế tick chọn checkbox từng món hàng trong giỏ để tính tiền và mang đi checkout, các món không tick vẫn giữ nguyên trong giỏ. |
| **Two-Phase Placement** | Chuỗi đặt hàng 2 pha: Pha 1 lấy `quoteId`, Pha 2 gửi `quoteId` để trừ tiền và khóa kho vật lý. |
| **Asymmetric Price Drift** | Chính sách xử lý biến động giá: Sản phẩm tăng giá -> Chặn tạo đơn; Sản phẩm giảm giá -> Tự động áp dụng giá rẻ hơn cho người mua. |
| **Idempotency Key** | Chuỗi UUID chống click lặp hoặc gửi trùng request thanh toán/tạo đơn. |

---

## 2. QUY ƯỚC CHUNG KHI GỌI API (GENERAL CONVENTIONS)

### 2.1. Base URL
Trong các môi trường triển khai, FE sử dụng biến môi trường:
- Môi trường Phát triển (Local / Dev): `http://localhost:8003` (hoặc qua Gateway cổng `80` / `443`).
- Định dạng biến môi trường FE: `VITE_COMMERCE_API_URL` hoặc `NEXT_PUBLIC_COMMERCE_API_URL`.
- Tất cả API đều có tiền tố phiên bản: `/api/v1/...`.

### 2.2. HTTP Headers Bắt Buộc
Mọi request gửi từ Frontend (thông qua Gateway) bắt buộc phải mang các Header sau:

| Header Name | Kiểu dữ liệu | Bắt buộc | Ý nghĩa |
|---|---|---|---|
| `Authorization` | String | Có | JWT Token: `Bearer <access_token>` |
| `Content-Type` | String | Có | `application/json` |
| `Accept` | String | Có | `application/json` hoặc `application/problem+json` |
| `Idempotency-Key` | UUID String | Khuyên dùng | Dùng cho các API tạo đơn (`POST /orders`), thanh toán (`POST .../pay`), hoàn tiền (`POST .../refund`) để chống bấm đúp |

*(Lưu ý: Header `x-user-id` và `x-user-roles` do API Gateway tự động giải mã từ JWT và chuyển tiếp xuống service, FE chỉ cần gửi đúng `Authorization: Bearer <token>`)*.

### 2.3. Định Dạng Phân Trang Chuẩn (`PagedData<T>`)
Tất cả các API danh sách (Đơn hàng, Đơn con, Voucher, Hoàn tiền) đều phân trang trực tiếp tại Database và trả về cấu trúc đồng nhất:

```json
{
  "items": [ ... ],
  "pagination": {
    "pageIndex": 1,
    "pageSize": 20,
    "totalCount": 45,
    "totalPages": 3,
    "hasPreviousPage": false,
    "hasNextPage": true
  }
}
```

### 2.4. Tiền Tệ & Định Dạng Ngày Giờ
- **Tiền tệ:** Toàn bộ số tiền (`TotalAmount`, `UnitPrice`, `ShippingFeeAmount`...) đều là số nguyên hoặc số thực dương, đơn vị tính mặc định là **Việt Nam Đồng (VND)**.
- **Ngày giờ:** 100% thời gian trả về dưới định dạng **ISO-8601 UTC** kết thúc bằng ký tự `Z` (ví dụ: `"2026-11-27T08:30:00Z"`). FE có trách nhiệm format sang múi giờ địa phương của người dùng (GMT+7).

### 2.5. Định Dạng Phản Hồi Lỗi Chuẩn (RFC 9110 Problem Details)
Khi gặp lỗi (HTTP Status 4xx, 5xx), response luôn trả về Content-Type `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Business rule violation",
  "status": 400,
  "errorCode": "INSUFFICIENT_STOCK",
  "detail": "Requested quantity exceeds available warehouse stock.",
  "reasonCode": null,
  "traceId": "00-845187e5b306b3bc4f3583cbfca5cb3a-01",
  "timestamp": "2026-11-27T08:30:00Z"
}
```
- FE dựa vào trường **`errorCode`** (`SCREAMING_SNAKE_CASE`) để phân nhánh hiển thị UI hoặc xử lý logic.
- Trường **`detail`** luôn là câu tiếng Anh viết hoa chữ cái đầu (Sentence case) để ghi log hoặc hiển thị fallback.

---

## 3. MA TRẬN PHÂN QUYỀN VAI TRÒ (RBAC MATRIX)

| Phân hệ chức năng | Chức năng chi tiết | `buyer` | `seller` | `staff` | `admin` |
|---|---|:---:|:---:|:---:|:---:|
| **Giỏ hàng (`/carts`)** | Xem, thêm, sửa, đổi variant, tính tiền, gộp giỏ | ✅ | ❌ | ❌ | ❌ |
| | Tra cứu giỏ hàng theo `buyerId` | ❌ | ❌ | ✅ | ✅ |
| **Báo giá & Đặt hàng** | Lấy báo giá xem trước (`/quote`), tính lại ship | ✅ | ❌ | ❌ | ❌ |
| | Chốt đặt đơn hàng chính thức (`POST /orders`) | ✅ | ❌ | ❌ | ❌ |
| | Lịch sử đơn hàng của tôi (`GET /orders`) | ✅ | ❌ | ❌ | ❌ |
| | Xem chi tiết đơn hàng cha (`GET /orders/{id}`) | ✅ (chủ đơn) | ❌ | ✅ | ✅ |
| | Thanh toán lại đơn hàng quá hạn (`POST /orders/{id}/pay`) | ✅ | ❌ | ❌ | ❌ |
| | Đổi địa chỉ nhận hàng sau khi đặt | ✅ (chủ đơn) | ❌ | ✅ | ✅ |
| **Đơn con (`/sub-orders`)** | Quản lý danh sách đơn con của Shop | ❌ | ✅ (chủ shop) | ✅ | ✅ |
| | Xem chi tiết đơn con & Phân rã tài chính shop | ❌ | ✅ (chủ shop) | ✅ | ✅ |
| | Tiếp nhận đơn (`Accept` -> sinh mã GHN) | ❌ | ✅ (chủ shop) | ❌ | ❌ |
| | Từ chối đơn (`Reject` -> hoàn tiền 100%) | ❌ | ✅ (chủ shop) | ❌ | ❌ |
| | Thử lại tạo vận đơn GHN khi lỗi mạng | ❌ | ✅ (chủ shop) | ✅ | ✅ |
| | Xác nhận đã nhận hàng (`Confirm Receipt`) | ✅ (chủ đơn) | ❌ | ❌ | ❌ |
| | Hủy đơn hàng con trước khi giao | ✅ (chủ đơn) | ❌ | ❌ | ❌ |
| **Đấu giá (`/auction-orders`)** | Winner nộp địa chỉ GHN & Thanh toán ví | ✅ (winner) | ❌ | ❌ | ❌ |
| | Seller tiếp nhận trong 24h hoặc từ chối | ❌ | ✅ (chủ shop) | ❌ | ❌ |
| | Tra cứu danh sách đơn đấu giá của tôi | ✅ (của mình) | ✅ (của shop) | ✅ | ✅ |
| **Khuyến mãi (`/discounts`)** | Xem voucher khả dụng, validate, recommend | ✅ | ❌ | ❌ | ❌ |
| | Tạo, sửa, bật/tắt, xóa voucher gian hàng | ❌ | ✅ (Shop voucher) | ❌ | ✅ (Toàn sàn) |
| **Hoàn tiền (`/refunds`)** | Tạo yêu cầu hoàn tiền đơn con | ✅ (chủ đơn) | ❌ | ❌ | ❌ |
| | Duyệt hoặc từ chối hoàn tiền | ❌ | ✅ (chủ shop) | ✅ | ✅ |
| **Quản trị Sàn (`/admin`)** | Tra cứu danh sách đơn cha & đơn con toàn sàn | ❌ | ❌ | ✅ | ✅ |
| | Cập nhật % tỷ lệ phí sàn hoa hồng | ❌ | ❌ | ✅ | ✅ |
| | Thu hồi điểm phạt boom hàng của người mua | ❌ | ❌ | ❌ | ✅ (Admin only) |
| | Phê duyệt ca bồi hoàn quỹ bảo hiểm sàn (2 cấp) | ❌ | ❌ | ❌ | ✅ (Admin only) |

---

## 4. MỤC LỤC & ĐIỀU HƯỚNG CÁC TÀI LIỆU LUỒNG CHI TIẾT

Mỗi luồng nghiệp vụ lớn được biên soạn chi tiết thành một tài liệu độc lập với đầy đủ `sequenceDiagram`, bảng thông số request/response, ví dụ JSON và ma trận mã lỗi:

1. 🛒 [**Luồng 1: Quản lý Giỏ hàng (Cart Flow)**](flows/01_CART_FLOW.md):
   - Thêm sản phẩm, cập nhật số lượng, đổi biến thể SKU trực tiếp.
   - Phân nhóm giỏ hàng theo Shop (`/grouped`).
   - Checkbox tính tiền tạm tính (Shopee selection) & Pre-checkout validation.
   - Gộp giỏ khách vãng lai khi đăng nhập.
2. 📦 [**Luồng 2: Báo giá & Đặt hàng Đa Người Bán (Checkout & Order Flow)**](flows/02_CHECKOUT_ORDER_FLOW.md):
   - Quy trình đặt hàng 2 pha (`Quote` TTL 15m -> `Create Order`).
   - Cơ chế phòng chống click đúp (Redis Atomic Pop).
   - Khóa giữ kho 15 phút & thanh toán ví Nexus trực tiếp.
   - Seller tiếp nhận đơn (sinh vận đơn GHN) hoặc từ chối (hoàn tiền ví 100%).
   - Theo dõi tiến độ giao hàng & timeline GHN thời gian thực.
   - Xác nhận nhận hàng và tự động giải ngân Escrow sang ví Shop.
3. 🔨 [**Luồng 3: Đơn hàng Đấu giá (Auction Order Flow)**](flows/03_AUCTION_ORDER_FLOW.md):
   - Nhận settlement từ phiên đấu giá, quản lý tiền đặt cọc.
   - Winner nộp địa chỉ, tính phí ship GHN, thanh toán số tiền còn lại.
   - Đếm ngược 24 giờ cho Seller tiếp nhận.
   - Nhận hàng, giải ngân Escrow và hoàn tiền khi hủy đơn.
4. 🏷️ [**Luồng 4: Khuyến mãi & Voucher Đa Tầng (Discount Flow)**](flows/04_DISCOUNT_PROMOTION_FLOW.md):
   - Danh sách voucher khả dụng kèm lý do không đủ điều kiện (`/available`).
   - Thẩm định mã voucher (`/validate`).
   - Gợi ý combo voucher tối ưu nhất (`/recommend`).
   - Quản lý voucher dành cho Seller và Admin.
5. 💰 [**Luồng 5: Hoàn tiền & Tranh chấp (Refund Flow)**](flows/05_REFUND_FLOW.md):
   - Tạo yêu cầu hoàn tiền đơn con.
   - Người bán / Admin duyệt hoặc từ chối kèm lý do.
   - Dòng tiền hoàn ví tự động.
6. ⚙️ [**Luồng 6: Nghiệp vụ Quản trị Sàn (Admin Operations Flow)**](flows/06_ADMIN_OPERATIONS_FLOW.md):
   - Quản lý % phí sàn hoa hồng.
   - Thu hồi án phạt boom hàng đối với người mua.
   - Phê duyệt bồi hoàn từ quỹ bảo hiểm sàn có cơ chế duyệt 2 cấp (Dual Approval).
   - Tra cứu đơn hàng toàn sàn.

---

## 5. REALTIME, POLLING & CƠ CHẾ CẬP NHẬT TRẠNG THÁI

### 5.1. Hiện Trạng Realtime trong CommerceService
- **Không có WebSocket / SignalR Server trực tiếp trong CommerceService:** Dịch vụ hoạt động theo mô hình Stateless RESTful microservice.
- **Cơ chế Thông báo Outbox (Asynchronous Notifications):**
  - Khi đơn hàng được thanh toán hoặc cập nhật trạng thái quan trọng, CommerceService ghi nhận sự kiện vào bảng Outbox và `NotificationOutboxWorker` sẽ đẩy sang `NotificationService`.
  - Khách hàng nhận được thông báo qua **Push Notification (FCM trên Mobile)** hoặc **Email**.

### 5.2. Khuyến Nghị Xử Lý Cho Frontend
1. **Tại Màn hình Đặt hàng (Checkout):**
   - Sau khi bấm `POST /orders`, nhận ngay kết quả HTTP 201 -> Điều hướng tức thời sang trang "Đặt hàng thành công".
   - Nếu gặp lỗi mạng tạm thời (Timeout), đơn giữ ở `PendingPayment`: Hiển thị nút "Kiểm tra trạng thái thanh toán" gọi `GET /api/v1/orders/{orderId}`.
2. **Tại Màn hình Chi tiết Đơn hàng / Timeline Vận Chuyển:**
   - Áp dụng cơ chế **Short Polling** (chu kỳ 15s - 30s một lần khi màn hình đang mở) để cập nhật tiến độ giao vận GHN.
   - Hỗ trợ thao tác kéo để làm mới (Pull-to-refresh) trên ứng dụng Mobile.
3. **Tại Màn hình Seller Tiếp Nhận Đơn:**
   - Nếu sau khi bấm "Xác nhận đơn" mà hệ thống trả về cờ `shipmentPendingCreation = true` (do GHN quá tải tạm thời), FE hiển thị nút bấm: `"Thử tạo lại vận đơn"` trỏ vào `POST /api/v1/sub-orders/{subOrderId}/shipments/retry`.

---

## 6. PHỤ LỤC ENUMS TOÀN HỆ THỐNG (CANONICAL ENUMS)

### 6.1. Trạng Thái Đơn Hàng Cha (`OrderStatus`)
| Enum Name | Giá trị số | Ý nghĩa nghiệp vụ |
|---|:---:|---|
| `PendingPayment` | 0 | Đang chờ thanh toán (chưa trừ tiền ví hoặc đang chờ COD xác nhận) |
| `PaymentProcessing` | 1 | Giao dịch thanh toán đang được xử lý |
| `Paid` | 2 | Đã thanh toán tiền hàng thành công qua Ví |
| `Cancelled` | 3 | Đơn hàng đã bị hủy (do người mua, người bán hoặc hết hạn) |
| `Expired` | 4 | Đơn hàng quá hạn thanh toán 15 phút (`PaymentDeadlineAt`) |
| `Completed` | 5 | Toàn bộ các đơn con đã giao thành công và hoàn tất |
| `Delivered` | 6 | Tất cả các đơn con đã được đơn vị vận chuyển giao tới người mua |

### 6.2. Trạng Thái Đơn Hàng Con (`SubOrderStatus`)
| Enum Name | Giá trị số | Ý nghĩa nghiệp vụ |
|---|:---:|---|
| `PendingPayment` | 0 | Đang chờ thanh toán |
| `Paid` | 1 | Đã thanh toán, chờ Người bán bấm xác nhận |
| `Confirmed` | 2 | Người bán đã xác nhận đơn, đã tạo vận đơn GHN (alias `SellerAccepted`) |
| `Shipped` | 3 | Đơn vị vận chuyển GHN đã lấy hàng và đang giao (alias `Shipping`) |
| `Delivered` | 4 | GHN đã giao hàng thành công đến tay người mua |
| `Completed` | 5 | Người mua đã xác nhận nhận hàng, tiền Escrow đã giải ngân cho Shop |
| `Cancelled` | 6 | Đơn con đã bị hủy |
| `Refunded` | 7 | Đơn con đã được hoàn tiền thành công cho người mua |
| `Returning` | 8 | Giao hàng thất bại, bưu tá đang hoàn hàng về cho Shop |

### 6.3. Phương Thức Thanh Toán (`PaymentMethod`)
| Enum Name | Giá trị số | Ý nghĩa |
|---|:---:|---|
| `Wallet` | 0 | Thanh toán qua Ví điện tử Nexus |
| `VnPay` | 1 | ⚠️ *Đã ngừng hoạt động (`[Obsolete]`)* |
| `Cod` | 2 | Thanh toán tiền mặt khi nhận hàng (Cash On Delivery) |

### 6.4. Lý Do Hủy Đơn (`CancellationReasonCode`)
| Enum Name | Giá trị số | Ý nghĩa |
|---|:---:|---|
| `BuyerChangedMind` | 0 | Người mua đổi ý không muốn mua nữa |
| `OutOfStock` | 1 | Người bán hết hàng |
| `PaymentTimeout` | 2 | Quá hạn 15 phút thanh toán |
| `SellerRejected` | 3 | Người bán từ chối nhận đơn |
| `Other` | 4 | Lý do khác |
| `DeliveryFailed` | 5 | Giao hàng thất bại nhiều lần |
| `PackageReturned` | 6 | Kiện hàng đã được hoàn trả về kho shop |
| `CancellationWithoutReason` | 7 | Hủy đơn không cần nêu lý do |

### 6.5. Trạng Thái Hoàn Tiền (`RefundStatus`)
| Enum Name | Giá trị số | Ý nghĩa |
|---|:---:|---|
| `Requested` | 0 | Người mua vừa tạo yêu cầu hoàn tiền, chờ duyệt |
| `Completed` | 1 | Tiền đã được hoàn trả thành công vào ví người mua |
| `Failed` | 2 | Hoàn tiền thất bại do sự cố kỹ thuật |
| `Rejected` | 3 | Người bán hoặc Admin từ chối yêu cầu hoàn tiền |
| `WalletPending` | 4 | Đã duyệt hoàn tiền, đang chờ Ví xác nhận giao dịch |

---

## 7. CÂU HỎI MỞ & ĐIỂM CẦN XÁC NHẬN (OPEN QUESTIONS)

1. ⚠️ **Cơ chế Realtime qua Gateway:**
   - *Hiện trạng:* CommerceService không có WebSocket server riêng.
   - *Cần xác nhận với Gateway/Infra Team:* Hệ thống microservice có dùng chung một Central Notification Gateway (WebSocket / Server-Sent Events) đẩy event cho Web/App hay yêu cầu FE thực hiện Polling định kỳ?
2. ⚠️ **Thời Hạn Tự Động Hoàn Tất Đơn Hàng (Auto-Completion Window):**
   - *Hiện trạng:* Khi bưu tá GHN giao thành công, đơn chuyển sang `Delivered`. Việc chuyển sang `Completed` (giải ngân Escrow) hiện phụ thuộc vào nút bấm của Buyer.
   - *Cần xác nhận với Product Team:* Sau bao nhiêu ngày (ví dụ: 3 ngày hoặc 7 ngày) kể từ khi `Delivered` thì hệ thống sẽ có worker tự động chuyển sang `Completed` nếu người mua không bấm và không khiếu nại?
3. ⚠️ **Quy Định Giới Hạn Hủy Đơn Đối Với Phương Thức COD:**
   - *Hiện trạng:* Đơn COD chuyển ngay sang `Confirmed` hoặc `PendingPayment`.
   - *Cần xác nhận:* Người mua có quyền tự hủy đơn COD tối đa trong bao lâu trước khi người bán bàn giao hàng cho shipper?
