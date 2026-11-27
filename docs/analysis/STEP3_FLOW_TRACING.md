# BƯỚC 3: TRUY VẾT LUỒNG NGHIỆP VỤ VÀ MA TRẬN PHỤ THUỘC (FLOW TRACING & DEPENDENCY MATRIX)

> **Ngày thực hiện:** 2026-11-27  
> **Tài liệu tạo lập:** `docs/analysis/STEP3_FLOW_TRACING.md`  

---

## 1. TRUY VẾT CÁC LUỒNG NGHIỆP VỤ CHÍNH (END-TO-END FLOW TRACES)

### 1.1. Luồng 1: Thêm vào Giỏ hàng & Đồng bộ Trạng thái (`Cart Flow`)
1. **Request từ FE:** `POST /api/v1/carts/me/items` với `{ skuId, quantity, productId }`.
2. **Kiểm tra nghiệp vụ nội bộ:**
   - Kiểm tra `quantity > 0`.
   - Giới hạn tối đa 50 mặt hàng trong giỏ, tối đa 99 chiếc/món.
3. **Gọi ra bên ngoài (Outbound REST):**
   - Gọi `CatalogService` (`GET /api/v1/catalog/skus/{skuId}`): Lấy `UnitPrice`, `ProductName`, trạng thái khả dụng `IsPurchasable`.
   - Gọi `FulfillmentService`: Kiểm tra tồn kho khả dụng `AvailableStock >= Quantity`.
4. **Cập nhật Persistence:**
   - Nếu SKU đã có trong giỏ -> dồn số lượng `Quantity += newQuantity`.
   - Nếu là món mới -> chèn thêm `CartItem`, lưu snapshot đơn giá `UnitPriceSnapshot`.
   - Cập nhật `SubtotalAmount` và `LastActivityAt` của `Cart`.
5. **Trạng thái cuối cùng:** Giỏ hàng `Active`, trả về toàn bộ giỏ hàng mới nhất cho FE cập nhật giao diện.

---

### 1.2. Luồng 2: Xem trước Báo giá & Đặt hàng Đa Người Bán (`Checkout & Order Flow`)
1. **Pha 1: Tạo Báo giá (Quote):**
   - FE gọi `POST /api/v1/checkouts/quote` với danh sách `CartItemIds`, địa chỉ giao hàng (`ShippingAddressInput`), mã voucher (`VoucherCodes`), phương thức thanh toán (`Wallet` hoặc `Cod`).
   - CommerceService:
     - Thẩm định quyền mua qua `UserService` (`CheckBuyerPermissionAsync`).
     - Gom nhóm các mặt hàng theo `SellerId`.
     - Với mỗi Shop, gọi `FulfillmentService` (`POST /shipping/fee`) tính cước GHN thực tế dựa trên địa chỉ kho của Shop và địa chỉ nhận của Buyer.
     - Tính toán voucher qua `VoucherCalculationEngine` (áp dụng phân tầng: Product -> Shop -> System).
     - Lưu kết quả báo giá vào Redis Cache với key `checkout_quote:{quoteId}`, TTL 15 phút.
     - Trả về `CheckoutQuoteResponse` chứa `quoteId`, hạn hết hạn `ExpiredAt`, chi tiết từng Shop, tổng tiền `GrandTotal`.
2. **Pha 2: Chốt Đơn Hàng Chính Thức (Order Placement):**
   - FE gọi `POST /api/v1/orders` với `quoteId`, `paymentMethod`, `shippingAddress`, `contactPhone`, `expectedShippingFee`.
   - **Atomic Pop Redis:** Đọc và xóa ngay `quoteKey` khỏi Redis Cache để ngăn chặn gửi trùng (click đúp).
   - **Kiểm tra biến động giá bất đối xứng (Asymmetric Price Drift):**
     - Gọi batch sang `CatalogService` lấy đơn giá thời gian thực.
     - Nếu có sản phẩm **tăng giá** -> Chặn đứng giao dịch, cập nhật lại snapshot giỏ hàng, ném lỗi `409 PRICE_CHANGED`.
     - Nếu sản phẩm **giảm giá** -> Âm thầm áp dụng giá mới ưu đãi hơn cho người mua, trả về thông tin `appliedPriceDrops`.
   - **Giữ chỗ Voucher:** Gọi `VoucherLifecycleService.ReserveVouchersAsync`.
   - **Khóa giữ kho vật lý (Inventory Reservation):** Gọi `FulfillmentService.ReserveStockAsync` giữ tồn kho với TTL 15 phút.
   - **Thanh toán Ví (Nếu chọn phương thức Wallet):**
     - Gọi `WalletService.PayOrderAsync(orderId, buyerId, totalAmount)`.
     - *Nếu thành công:* Chuyển đơn sang `OrderStatus.Paid`, `SubOrder.Status = SubOrderStatus.Paid`. Gọi `FulfillmentService.CommitStockAsync` để trừ kho vĩnh viễn. Đẩy thông báo outbox.
     - *Nếu thất bại dứt khoát (hết tiền, không tìm thấy ví):* Xả kho tại FulfillmentService, nhả voucher, giữ nguyên giỏ hàng, trả lỗi `400 INSUFFICIENT_WALLET_BALANCE`.
     - *Nếu lỗi mạng tạm thời (Timeout/5xx):* Giữ đơn ở trạng thái `PendingPayment`, giữ kho, chờ worker quét đối soát hoặc cho phép người mua bấm thanh toán lại (`POST /orders/{id}/pay`).
   - **Phương thức COD:** Trừ kho vĩnh viễn ngay lập tức (`CommitStockAsync`), đơn chuyển sang `SubOrderStatus.PendingPayment` hoặc `Confirmed`.
   - **Dọn dẹp giỏ hàng:** Tự động xóa các món vừa mua khỏi `CartItems`.
   - **Cơ chế Bồi hoàn Phân tán (Distributed Compensation):** Nếu việc lưu CSDL của CommerceService gặp lỗi sau khi đã trừ tiền ví và giữ kho, hệ thống tự động:
     1. Giải phóng kho tại `FulfillmentService`.
     2. Hoàn lại 100% tiền vào ví tại `WalletService` với key `compensation-db-{orderId}`.
     3. Trả lại lượt voucher.
     4. Trả về lỗi HTTP `500 ORDER_CREATION_FAILED`.

---

### 1.3. Luồng 3: Vòng đời Xử lý Đơn Hàng Con của Người Bán (`Seller SubOrder Lifecycle`)
1. **Người bán Tiếp Nhận Đơn (`POST /api/v1/orders/{orderId}/accept`):**
   - Điều kiện: Đơn con đang ở trạng thái `Paid` (hoặc `PendingPayment` với COD).
   - Cập nhật `SubOrderStatus = SubOrderStatus.Confirmed`.
   - Tự động gọi `FulfillmentService.CreateShipmentAsync` để đẩy thông tin sang GHN và lấy mã vận đơn `TrackingCode`.
   - Nếu GHN gặp sự cố mạng tạm thời, đánh dấu `ShipmentPendingCreation = true`. Người bán có thể gọi lại API `POST /api/v1/sub-orders/{subOrderId}/shipments/retry`.
2. **Người bán Từ Chối Đơn (`POST /api/v1/orders/{orderId}/reject`):**
   - Chuyển `SubOrderStatus = SubOrderStatus.Cancelled`, lý do `SellerRejected`.
   - Tự động hoàn tiền 100% về ví cho Người mua (`WalletService.RefundOrderPaymentAsync`).
   - Nhả tồn kho tại `FulfillmentService`.
   - Ghi nhận vi phạm hủy đơn và phạt điểm uy tín người bán qua `UserService`.
3. **Cập nhật Tiến độ Giao hàng từ GHN (Webhook Callback):**
   - GHN thông báo cho `FulfillmentService` -> `FulfillmentService` gọi webhook `POST /api/v1/sub-orders/{subOrderId}/delivery-status`.
   - Khi trạng thái là `PickedUp`: Chuyển `SubOrderStatus = SubOrderStatus.Shipped`.
   - Khi trạng thái là `Delivered`: Chuyển `SubOrderStatus = SubOrderStatus.Delivered`.
   - Khi giao thất bại (bom hàng): Chuyển `SubOrderStatus = SubOrderStatus.Returning` -> `Cancelled`.
4. **Xác nhận Nhận Hàng & Giải Ngân Quỹ Ủy Thác (`Confirm Receipt & Escrow Release`):**
   - Người mua bấm "Đã nhận được hàng": `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt`.
   - Chuyển `SubOrderStatus = SubOrderStatus.Completed`.
   - Tự động gọi `WalletService.ReleaseEscrowAsync` giải ngân số tiền `SellerReceiveAmount` sang ví của Người bán. Đánh dấu `IsEscrowSettled = true`.
   - Thưởng điểm uy tín cho các bên qua `UserService`.

---

### 1.4. Luồng 4: Đơn Hàng Đấu Giá (`Auction Order Flow`)
1. **Tiếp nhận Settlement (`POST /api/v1/auction-orders/settlement`):**
   - `AuctionService` gửi kết quả phiên đấu giá (WinnerId, WinningAmount, DepositAmount, Deadline).
   - CommerceService kiểm tra tính duy nhất qua composite key `AuctionId + WinnerId`. Nếu đã có, trả lại bản ghi cũ; nếu chưa, tạo `AuctionOrder` với trạng thái `PendingPayment`.
2. **Winner Nộp Địa Chỉ Giao Hàng (`PUT /api/v1/auction-orders/{id}/shipping`):**
   - Winner gửi địa chỉ GHN -> CommerceService gọi `FulfillmentService` tính phí ship GHN.
   - Chốt công thức: `TotalAmount = WinningAmount + ShippingFeeAmount - DepositAmount` (Số tiền thực tế còn phải thanh toán).
3. **Winner Thanh Toán (`POST /api/v1/auction-orders/{id}/pay`):**
   - Gọi `WalletService` trừ số tiền còn lại.
   - Đổi trạng thái sang `SubOrderStatus.Paid`. Thiết lập hạn chót tiếp nhận cho Seller là 24 giờ (`SellerRespondDeadlineAt = UtcNow + 24h`).
4. **Seller Tiếp Nhận / Từ Chối:**
   - Seller Accept -> `SubOrderStatus.Confirmed`, tự động tạo vận đơn GHN.
   - Seller Reject (hoặc quá 24h bị timeout) -> `SubOrderStatus.Cancelled`, hoàn lại 100% tiền hàng và tiền cọc vào ví người mua.
5. **Hoàn tất:** Khi giao thành công, người mua xác nhận nhận hàng -> `Completed`, giải ngân tiền hàng cho Seller.

---

## 2. MA TRẬN PHỤ THUỘC GIAO TIẾP NGOẠI VI (OUTBOUND DEPENDENCY MATRIX)

| Dịch vụ đích | Giao thức | API Endpoint gọi sang | Mục đích nghiệp vụ | Cơ chế chịu lỗi / Timeout / Retry | Bù trừ khi lỗi (Compensation) |
|---|---|---|---|---|---|
| **`WalletService`** | REST (HTTP) | `POST /api/v1/wallets/checkout/direct` | Trừ tiền thanh toán đơn hàng | Polly retry 3 lần, Timeout 10s, Fail-Closed | Nếu DB save lỗi, gọi `POST /refund/direct` hoàn lại 100% |
| **`WalletService`** | REST (HTTP) | `POST /api/v1/wallets/refund/direct` | Hoàn tiền ví khi hủy/từ chối đơn | Polly retry, idempotent qua `IdempotencyKey` | Đưa vào hàng chờ `SettlementRetryWorker` nếu timeout |
| **`WalletService`** | REST (HTTP) | `POST /api/v1/wallets/escrow/release` | Giải ngân tiền hàng cho Seller | Retry qua `SettlementRetryWorker` | Đánh dấu `IsEscrowSettled = false` để đối soát |
| **`FulfillmentService`** | REST (HTTP) | `POST /api/v1/fulfillment/shipping/fee` | Tính cước GHN theo địa chỉ | Timeout 5s, fallback phí mặc định hoặc báo lỗi | Không ảnh hưởng tài chính |
| **`FulfillmentService`** | REST (HTTP) | `POST /api/v1/fulfillment/inventory/reserve` | Giữ tồn kho vật lý 15 phút | Fail-Closed (nếu hết hàng hoặc lỗi -> dừng tạo đơn) | Nhả tồn kho qua `POST .../inventory/release` |
| **`FulfillmentService`** | REST (HTTP) | `POST /api/v1/fulfillment/inventory/commit` | Xác nhận trừ kho vĩnh viễn | Retry tự động | Ghi log bất đối soát nếu commit thất bại sau khi trừ tiền |
| **`FulfillmentService`** | REST (HTTP) | `POST /api/v1/fulfillment/inventory/release` | Giải phóng hàng tồn khi hủy đơn | Idempotent | Thử lại định kỳ qua worker |
| **`FulfillmentService`** | REST (HTTP) | `POST /api/v1/fulfillment/shipments` | Tạo vận đơn GHN khi Seller duyệt | Fail-Open có cờ retry (`ShipmentPendingCreation = true`) | Seller có thể bấm nút Retry từ giao diện |
| **`CatalogService`** | REST (HTTP) | `GET /api/v1/catalog/skus/{id}` / batch | Lấy giá niêm yết và trạng thái SKU | Cache nhẹ, phát hiện tăng giá để chặn gian lận | Không phát sinh chi phí |
| **`CatalogService`** | REST (HTTP) | `POST /api/v1/catalog/variants/mark-ordered` | Đánh dấu SKU đã có đơn (khóa xóa) | Best-effort, ghi warning nếu lỗi | Không ảnh hưởng đến khách hàng |
| **`UserService`** | REST (HTTP) | `GET /api/v1/internal/users/{id}/permission-check` | Kiểm tra quyền mua của tài khoản | Fail-Closed (chặn mua nếu bị khóa) | Không có |
| **`UserService`** | REST (HTTP) | `POST /api/v1/internal/users/reputation-adjust` | Thưởng điểm uy tín khi hoàn thành đơn | Best-effort | Không có |
| **`UserService`** | REST (HTTP) | `POST /api/v1/internal/users/penalties` | Phạt điểm uy tín khi hủy đơn vi phạm | Best-effort | Admin có quyền thu hồi phạt (`revoke`) |

---

## 3. CƠ CHẾ ĐỒNG BỘ DỮ LIỆU & TÍNH NHẤT QUÁN CUỐI CÙNG (EVENTUAL CONSISTENCY)

| Nghiệp vụ | Thời điểm FE nhận Response | Xử lý ngầm tiếp theo | Cơ chế FE nhận biết kết quả cuối cùng | Khuyến nghị cho Frontend |
|---|---|---|---|---|
| **Đặt hàng ví (`POST /orders`)** | Nhận ngay sau khi trừ tiền ví và lưu CSDL thành công | Kho được commit vĩnh viễn, voucher commit, thông báo gửi ngầm | Response trả về trực tiếp `status = "Paid"` | Điều hướng ngay tới màn hình "Đặt hàng thành công" |
| **Thanh toán ví gặp Timeout tạm thời** | Nhận sau khi gọi ví bị timeout | Đơn giữ ở `PendingPayment`, worker quét lại trạng thái giao dịch | Polling trạng thái đơn hàng (`GET /api/v1/orders/{id}`) | Hiển thị màn hình "Giao dịch đang xử lý", cho phép bấm kiểm tra lại |
| **Seller duyệt đơn (`POST /accept`)** | Nhận ngay sau khi đổi trạng thái sang `Confirmed` | Gọi GHN tạo mã vận đơn (có thể mất vài giây) | Polling hoặc reload trang chi tiết đơn con | Nếu `ShipmentPendingCreation = true`, hiển thị nút "Thử tạo lại vận đơn" |
| **Cập nhật vị trí giao hàng GHN** | GHN gửi webhook bất đồng bộ cho Fulfillment | Fulfillment gọi callback CommerceService cập nhật timeline | Polling timeline (`GET /api/v1/sub-orders/{id}/shipments/timeline`) | Hiển thị Timeline giao hàng, cập nhật mỗi khi user mở màn hình |
| **Giải ngân tiền hàng cho Seller** | Nhận ngay khi Buyer bấm "Đã nhận hàng" | Tiền được giải ngân sang ví Seller | `ConfirmReceiptResponse` trả về `escrowSettled: true` | Hiển thị thông báo hoàn tất, mở popup đánh giá sản phẩm |
| **Thu hồi phạt boom hàng (`/revoke`)** | Nhận ngay sau khi Admin bấm thu hồi | Nếu User Service chưa trả lời, trả HTTP 202 `Accepted` (`RevokePending`) | Polling hoặc xem lại trạng thái sau | Hiển thị trạng thái "Đang xử lý thu hồi điểm" |

---

## 4. CÂU HỎI MỞ & ĐIỂM CẦN XÁC NHẬN VỚI HỆ THỐNG NGOẠI VI

1. ⚠️ **Cơ chế Realtime (WebSocket / SSE):**
   - Hiện tại mã nguồn CommerceService **hoàn toàn không chứa Hub SignalR hay WebSocket server**.
   - Mọi thông báo giao hàng và biến động trạng thái đơn đều dựa trên cơ chế:
     - Worker đẩy sang `NotificationService` (để gửi Push Mobile/Email).
     - Frontend chủ động **Polling** (hoặc nhận event từ Central WebSocket Gateway nếu dự án có gateway riêng).
   - *Cần xác nhận với team Mobile/Web:* Có cổng WebSocket Gateway trung tâm hay client polling định kỳ 10-15s?
2. ⚠️ **Phương thức thanh toán VNPay:**
   - Trong code, `PaymentMethod.VnPay` đã bị đánh dấu `[Obsolete]` và bị chặn tại `CreateOrderAsync` với thông báo `"VNPay payment method is no longer supported. Please use Wallet or COD."`.
   - *FE cần lưu ý:* Tuyệt đối không hiển thị tùy chọn VNPay trên màn hình Checkout của ứng dụng.
3. ⚠️ **Chính sách tự động xác nhận đơn hàng khi GHN đã giao (Auto-Completion Policy):**
   - Hiện tại trạng thái `Delivered` do webhook cập nhật. Việc chuyển sang `Completed` chủ yếu do Buyer bấm `POST /confirm-receipt`. Cần xác nhận xem có worker nào tự động hoàn thành đơn sau 7 ngày nếu Buyer không bấm hay không.
