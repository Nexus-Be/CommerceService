# BÁO CÁO XÁC NHẬN API CONTRACT & ĐẶC TẢ TÍCH HỢP LIÊN DỊCH VỤ
## COMMERCE SERVICE ➔ AUCTION SERVICE
### (Chuẩn Hóa Theo Tài Liệu Tích Hợp Đặt Cọc v0.4 & Đính Chính Ngày 16/11/2026)

> **Người lập:** Team Commerce Service (`Commerce.Api:8003`)  
> **Người nhận:** Team Auction Service (`:8005`)  
> **Tài liệu tham chiếu:** `auction_to_commerce_v0.4` & Văn bản đính chính của Auction ngày 16/11/2026  
> **Ngày hoàn tất:** 2026-11-16  
> **Tình trạng kiểm thử:** ✅ ĐÃ KIỂM TRA CHÉO 100% CODEBASE & PASS 194/194 UNIT TESTS

---

## 1. BẢNG XÁC NHẬN ĐỐI CHIẾU NGHIỆP VỤ (REQUIREMENT VERIFICATION MATRIX)

Dưới đây là bảng đối chiếu xác nhận từng yêu cầu giữa tài liệu của Auction Service và source code thực tế của Commerce Service:

| STT | Hạng mục nghiệp vụ | Yêu cầu từ Auction Service (v0.4 & Đính chính) | Hiện trạng thực thi tại Commerce Service | Kết quả đối soát |
|:---:|:---|:---|:---|:---:|
| 1 | **Tiếp nhận cọc Winner** | Gửi `depositAmount >= 0`, `source = "WINNER"` qua `POST /settlement`. | `AuctionSettlementRequest` tiếp nhận và lưu trữ `DepositAmount`, `OrderSource = Winner`. | ✅ KHỚP 100% |
| 2 | **Tiếp nhận cọc Runner-Up** | Order `SECOND_CHANCE` **cũng có cọc `depositAmount >= 0`** (Auction hold). | Đã **gỡ bỏ hoàn toàn** rule & lỗi `INVALID_RUNNER_UP_DEPOSIT`. `SECOND_CHANCE` có cọc được xử lý bình thường. | ✅ KHỚP 100% |
| 3 | **Công thức tiền hàng** | $\text{Remaining} = \text{Winning} - \text{Deposit}$; $\text{Total} = \text{Remaining} + \text{Ship}$. | Triển khai chính xác tại `ReceiveSettlementAsync` và `SubmitShippingAsync`. | ✅ KHỚP 100% |
| 4 | **Tương thích ảnh sản phẩm** | Auction đổi từ `imageUrl` sang `productImageUrl`. | Code hỗ trợ song song: `ProductImageUrl = request.ProductImageUrl ?? request.ImageUrl`. | ✅ KHỚP 100% |
| 5 | **Thuộc tính SKU** | `skuAttributes` là tuỳ chọn, thiếu không báo lỗi 400. | Thuộc tính kiểu `string? SkuAttributes`, thiếu tự gán fallback `"{}"`. | ✅ KHỚP 100% |
| 6 | **Validation tài chính (422)** | Bắt lỗi `INVALID_AMOUNT` ($\le 0$) và `DEPOSIT_EXCEEDS_WINNING_AMOUNT`. | Kiểm tra nghiêm ngặt tại đầu vào `ReceiveSettlementAsync`. | ✅ KHỚP 100% |
| 7 | **Xử lý xung đột (409)** | Trả về `SETTLEMENT_CONFLICT` nếu phiên đã có đơn active khác. | Kiểm tra `activeOtherOrder != null` ném 409 `SETTLEMENT_CONFLICT`. | ✅ KHỚP 100% |
| 8 | **Tính lũy thừa (Idempotency)** | Trùng `(auctionId, source, winnerId)` trả lại đơn cũ, không tạo trùng. | Kiểm tra `identicalOrder != null` trả về HTTP 200 OK với thông tin đơn cũ. | ✅ KHỚP 100% |
| 9 | **Hoàn tiền độc lập** | Khi hủy đơn, Commerce chỉ hoàn tiền hàng + ship đã thu; Auction hoàn cọc qua W5. | `SellerRejectAsync` và `PaymentTimeoutWorker` hoàn strictly `TotalAmount ?? RemainingAmount`, không chạm vào cọc. | ✅ KHỚP 100% |
| 10 | **Ranh giới Catalog** | Khi `PaymentTimedOut`, Commerce **không tự nhả kho** tại Catalog. | `PaymentTimeoutWorker` chỉ chuyển đơn `Cancelled`, không gọi Catalog mở khóa. | ✅ KHỚP 100% |
| 11 | **Callback Outbound** | Gọi `POST /settlement/payment-status` có `orderSource` và `occurredAt`. | Đã bổ sung `orderSource` (`"WINNER"` / `"SECOND_CHANCE"`) và `occurredAt` (ISO 8601 UTC). | ✅ KHỚP 100% |
| 12 | **Phân định Phạt Uy Tín** | Khi `PaymentTimedOut`, Commerce **không phạt Buyer** (Auction gọi API 20). Khi `SellerRejected` / `SellerExpired`, Commerce phạt Seller 5 điểm. | Đã xóa lệnh gọi UserService cho Buyer trong `PaymentTimeoutWorker`. Giữ nguyên phạt Seller 5 điểm khi Seller từ chối/hết hạn. | ✅ KHỚP 100% |

---

## 2. INBOUND CONTRACT: `POST /api/v1/auction-orders/settlement`

- **Endpoint**: `POST /api/v1/auction-orders/settlement`
- **Xác thực**: Header `X-Internal-Service-Key`
- **Content-Type**: `application/json`

### 2.1. Request Schema (`AuctionSettlementRequest`)

```json
{
  "auctionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "winnerId": "11111111-1111-1111-1111-000000000004",
  "sellerId": "22222222-2222-2222-2222-000000000002",
  "winningBidId": "4fa85f64-5717-4562-b3fc-2c963f66af01",
  "productId": "8bbbbbbb-bbbb-bbbb-bbbb-000000000001",
  "skuId": null,
  "productName": "Đồng hồ cơ cổ Thụy Sĩ 1970",
  "productImageUrl": "https://cdn.nexus.local/images/watch.png",
  "skuAttributes": "{\"color\":\"gold\"}",
  "winningAmount": 2000000.00,
  "paymentDeadlineAt": "2026-11-17T10:00:00Z",
  "depositAmount": 200000.00,
  "source": "WINNER"
}
```

*Ghi chú tương thích*:
- Với người về nhì: Truyền `source: "SECOND_CHANCE"`, `winnerId: <RunnerUpGuid>`, và `depositAmount` bằng số tiền cọc đang bị hold của người về nhì đó.
- Nếu gửi tên trường ảnh là `imageUrl` thay vì `productImageUrl`, hệ thống vẫn tự động tiếp nhận.

### 2.2. Phản hồi thành công

#### A. Tạo mới thành công (HTTP `201 Created`)
```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderNo": "AO-20261116-4FA85F",
  "auctionId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "winnerId": "11111111-1111-1111-1111-000000000004",
  "status": "PendingPayment",
  "paymentDeadlineAt": "2026-11-17T10:00:00Z"
}
```

#### B. Thành công lặp lại / Idempotent (HTTP `200 OK`)
Khi gửi lại cùng bộ khóa `(auctionId, source, winnerId)`, Commerce trả về HTTP `200 OK` với body đơn hàng đã tạo trước đó.

---

## 3. OUTBOUND CALLBACK CONTRACT: COMMERCE ➔ AUCTION SERVICE

- **Endpoint**: `POST http://auction-service:8005/api/v1/auctions/{auctionId}/settlement/payment-status`
- **Header**: `X-Internal-Service-Key`
- **Content-Type**: `application/json`

### 3.1. Payload Schema (`AuctionPaymentStatusCallbackRequest`)

```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "winnerId": "11111111-1111-1111-1111-000000000004",
  "status": "Paid",
  "amount": 2000000.00,
  "paidAt": "2026-11-16T10:15:30Z",
  "occurredAt": "2026-11-16T10:15:30Z",
  "orderSource": "WINNER",
  "eventId": "e7b0c39f-2a31-4e4f-8cf9-0604ec22e001"
}
```

### 3.2. Bảng 5 Trạng Thái Callback & Xử Lý

| `status` | `orderSource` | `paidAt` | Hành động kích hoạt tại Commerce | Hành vi tương ứng của Auction Service |
|:---|:---:|:---:|:---|:---|
| `"Paid"` | `"WINNER"` / `"SECOND_CHANCE"` | Có | Buyer thanh toán số tiền còn thiếu qua ví thành công. | **Ngay lập tức thực hiện Thu cọc (Capture `ORDER_PAYMENT`)** chuyển cọc vào Escrow của order. |
| `"SellerAccepted"` | Cả hai | null | Seller duyệt đơn trong 24h (đã khởi tạo vận đơn GHN). | Khi đã có đủ cả `Paid`: chuyển phiên sang `Settled`, gọi Catalog khóa sản phẩm vĩnh viễn, mở cọc của những người khác. |
| `"SellerRejected"` | Cả hai | null | Seller từ chối đơn trong 24h (Commerce đã hoàn tiền hàng + ship cho Buyer và **trực tiếp gọi UserService trừ 5 điểm uy tín Seller**). | **Gọi Wallet W5 hoàn cọc đã thu** cho Buyer; mở cọc còn lại; phiên thất bại; nhả hàng Catalog. |
| `"SellerExpired"` | Cả hai | null | Worker tự hủy do Seller quá hạn duyệt 24h (Commerce đã hoàn tiền hàng + ship và **trực tiếp gọi UserService trừ 5 điểm uy tín Seller**). | **Gọi Wallet W5 hoàn cọc đã thu** cho Buyer; mở cọc còn lại; phiên thất bại; nhả hàng Catalog. |
| `"PaymentTimedOut"` | `"WINNER"` | null | Worker tự hủy do Winner quá hạn thanh toán 24h. **Commerce KHÔNG gọi UserService trừ uy tín Buyer** (tránh phạt trùng 2 lần). | Tịch thu cọc Winner; **gọi UserService API 20 (`auction-violations`) phạt uy tín & tăng streak của Buyer**. Nếu có runner-up: gửi settlement mới với `source = "SECOND_CHANCE"`. Nếu không: nhả hàng Catalog. |
| `"PaymentTimedOut"` | `"SECOND_CHANCE"` | null | Worker tự hủy do runner-up quá hạn thanh toán 24h. **Commerce KHÔNG gọi UserService trừ uy tín Buyer**. | **Gọi UserService API 20 xử lý vi phạm runner-up**; phiên thất bại; nhả hàng Catalog. |

---

## 4. GIẢI ĐÁP CHÍNH THỨC 9 CÂU HỎI KỸ THUẬT TỪ AUCTION SERVICE

### 1. Callback gửi lỗi (Auction tắt, timeout) thì Commerce có thử lại không?
- **Hiện trạng**: Commerce gọi HTTP POST với timeout 10 giây. Nếu mạng lỗi hoặc 5xx, Commerce log cảnh báo và không retry tự động (fail-safe để tránh ảnh hưởng giao dịch tiền ví của người dùng). Worker `PaymentTimeoutWorker` khi hủy đơn đã chuyển trạng thái `Cancelled` ngay, nên chu kỳ quét sau (mỗi 60s) sẽ không quét lại đơn đó.
- **Biện pháp phối hợp**: Phía Auction đảm bảo endpoint callback có độ khả dụng cao. Phía Commerce sẽ cấu hình thêm Polly retry 3 lần (backoff 2s, 4s, 8s) trên HttpClient khi gặp lỗi tạm thời.

### 2. Callback có thêm `occurredAt` (thời điểm xảy ra) được không?
- **Đã hoàn thành**: Trường `occurredAt` (ISO 8601 UTC) đã được bổ sung vào toàn bộ 5 sự kiện callback để Auction xác định thứ tự chuẩn xác.

### 3. Khi đơn bị Admin hủy hoặc Buyer tự hủy, Commerce có báo Auction không? Status nào?
- **Không có trường hợp này**: Commerce Service **không có API cho Buyer tự hủy hay Admin hủy đơn đấu giá**. Đơn đấu giá mang tính cam kết pháp lý. Đơn chỉ bị hủy qua 3 trường hợp: `SellerRejected`, `SellerExpired`, hoặc `PaymentTimedOut`. Cả 3 trường hợp này đều gửi callback tương ứng sang Auction.

### 4. Khi đơn hoàn tất (`Completed`), Commerce có cần báo Auction không?
- **Không cần thiết**: Vì Auction Service đã chốt phiên sang `Settled` ngay khi có đủ cả `Paid` và `SellerAccepted`, nên trạng thái `Completed` (sau khi giao hàng thành công) chỉ phục vụ việc Commerce giải ngân Escrow cho Seller, không làm thay đổi trạng thái phiên đấu giá nữa.

### 5. Với order `SECOND_CHANCE`, `winnerId` trong callback là ID người về nhì, đúng không?
- **Đúng 100%**: Trong code Commerce, `WinnerId` trong callback map từ `auctionOrder.BuyerId`. Khi nhận settlement cho người về nhì, Commerce gán `BuyerId` chính bằng `request.WinnerId` do Auction gửi sang.

### 6. Order người về nhì có hạn thanh toán 24h tính từ lúc tạo order, đúng không? Auction gửi `paymentDeadlineAt` hay Commerce tự tính?
- **Auction là nguồn sự thật**: Commerce lưu trữ trực tiếp giá trị `request.PaymentDeadlineAt` do Auction truyền sang. Auction chủ động tính toán hạn 24h từ lúc gửi settlement cho người về nhì.

### 7. Seller chỉ bấm đồng ý giao được sau `Paid`, đúng không?
- **Đúng 100%**: Code của Commerce kiểm tra nếu đơn chưa `Paid` thì chặn và trả lỗi 400 `INVALID_STATUS`. Do đó, thứ tự thực tế tại Commerce luôn là **Buyer `Paid` trước, sau đó Seller mới có thể `SellerAccepted`**.

### 8. Gửi settlement trùng nhưng khác số tiền hoặc khác Buyer: Commerce trả `SETTLEMENT_CONFLICT` (409)?
- **Đúng**:
  - Trùng khóa `(AuctionId, OrderSource, BuyerId)`: Trả về HTTP 200 OK (Idempotent, giữ nguyên đơn cũ).
  - Có đơn active khác: Trả về HTTP 409 `SETTLEMENT_CONFLICT`.
  - Đơn cũ đã `Cancelled`: Cho phép tạo đơn `SECOND_CHANCE` mới trả về HTTP 201 Created.

### 9. Khi giải ngân Seller (`ConfirmReceipt`), Commerce có kiểm escrow của order đã đủ `winningAmount` không? Nếu Auction chưa Capture xong cọc thì Commerce xử lý ra sao?
- **Thực tế**: Khi Buyer xác nhận nhận hàng, Commerce gọi Wallet Service giải ngân đúng 95% của `winningAmount` cho Seller (`SellerReceiveAmount`). Commerce không tự kiểm tra số dư Escrow trước.
- Nếu Auction chưa Capture cọc xong và Wallet Service từ chối giải ngân, Commerce bắt Exception, log lỗi và đơn vẫn giữ trạng thái `Completed` để chờ đối soát/giải ngân thủ công.
- **Yêu cầu đối với Auction**: Auction cần đảm bảo việc Capture cọc được hoàn tất ngay sau callback `Paid` (hoặc job quét của Auction phải xử lý xong trong 2-4 ngày giao vận trước khi Buyer nhận hàng).

---

## 5. TYPESCRIPT TYPE DEFINITIONS

```typescript
export interface AuctionSettlementRequest {
  auctionId: string;
  winnerId: string;
  sellerId: string;
  winningBidId: string;
  productId: string;
  skuId?: string | null;
  productName: string;
  productImageUrl?: string | null;
  skuAttributes?: string | null;
  winningAmount: number;
  paymentDeadlineAt: string;
  depositAmount?: number | null;
  source?: "WINNER" | "SECOND_CHANCE" | null;
  shippingAddress?: ShippingAddressDto | null;
  contactPhone?: string | null;
}

export interface AuctionSettlementResponse {
  orderId: string;
  orderNo: string;
  auctionId: string;
  winnerId: string;
  status: "PendingPayment";
  paymentDeadlineAt: string;
}

export interface AuctionPaymentStatusCallbackRequest {
  orderId: string;
  winnerId: string;
  status: "Paid" | "SellerAccepted" | "SellerRejected" | "SellerExpired" | "PaymentTimedOut";
  amount?: number | null;
  paidAt?: string | null;
  occurredAt: string; // ISO 8601 UTC
  orderSource?: "WINNER" | "SECOND_CHANCE" | null;
  eventId?: string | null;
}
```
