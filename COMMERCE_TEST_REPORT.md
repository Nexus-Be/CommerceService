# COMMERCE SERVICE — END-TO-END FLOW TESTING & AUDIT REPORT

> **Người thực hiện:** Senior QA Automation & Backend Engineer  
> **Dự án:** Commerce Service (Port `8003`, Database `commerce_db:5435`)  
> **Thời điểm xuất báo cáo:** 2026-11-02  
> **Runtime / Framework:** .NET 9.0.22 / C# 12 / xUnit 2.5.4 / FluentAssertions 6.12  
> **Trạng thái kiểm thử:** ✅ **100% PASSED (60/60 Test Cases — 0 Failures)**

---

## 1. TỔNG QUAN KẾT QUẢ KIỂM THỬ (EXECUTIVE SUMMARY)

Hệ thống **Commerce Service** đã trải qua đợt kiểm thử tự động toàn diện (Automated End-to-End & Integration Test Suite) bao quát toàn bộ 8 Flow nghiệp vụ trọng yếu của hệ sinh thái Thương mại điện tử (Shopping Cart, Checkout Quote, Asymmetric Price Drift, Order Creation with Stock & Wallet, Database Failure Compensation, Hybrid Payment Expiration, Seller Acceptance with GHN, và Seller Targeted Rejection).

### Bảng số liệu tổng hợp

| Chỉ số | Kết quả | Ghi chú |
| :--- | :---: | :--- |
| **Tổng số Flow nghiệp vụ E2E đã kiểm thử** | **8 / 8 Flow** | Kiểm thử xuyên suốt qua các tầng Controller $\rightarrow$ Service $\rightarrow$ DB $\rightarrow$ External Clients |
| **Tổng số Test Cases thực thi** | **60 Tests** | 8 E2E Flow Tests + 52 Unit / Integration / Fault Tolerance Tests |
| **Số Test Case ĐẠT (Passed)** | **60 Tests** | **Tỷ lệ thành công: 100.0%** |
| **Số Test Case LỖI (Failed)** | **0 Tests** | 0 lỗi hồi quy (Regression) |
| **Tổng thời gian thực thi (Execution Time)** | **2.5037 giây** | Cấu hình Release tối ưu hóa |
| **Trạng thái Build Solution (`dotnet build`)** | **Succeeded** | 0 Cảnh báo (Warnings), 0 Lỗi (Errors) |
| **Kết nối Database (`appsettings.json`)** | **Active & Reachable** | Neon PostgreSQL Pooler (`ep-bold-poetry-b501gcqr-pooler.c-7.us-east-2.aws.neon.tech:5432`) |

---

## 2. NHẬT KÝ CHI TIẾT THỰC THI TỪNG FLOW (FLOW EXECUTION LOG)

---

### Flow 1: Chu kỳ Sống Toàn diện của Giỏ Hàng (Shopping Cart Full Lifecycle)

* **Mục đích:** Kiểm thử toàn bộ thao tác thêm, cập nhật số lượng, đổi phân loại (variant), dồn số lượng, xóa từng món và xóa sạch giỏ hàng theo đúng các quy tắc bất biến của Domain.
* **Đầu vào (Request Payloads):**
  * `POST /api/v1/carts/items`: Thêm 2 món từ 2 người bán khác nhau:
    * Món 1: `SkuId = sku_1`, `Quantity = 2`, `SellerId = shop_A`
    * Món 2: `SkuId = sku_3`, `Quantity = 1`, `SellerId = shop_B`
  * `PUT /api/v1/carts/items/{itemId}/variant`: Đổi từ `sku_1` sang `sku_2` (cùng Shop A, giá 150.000đ).
  * `POST /api/v1/carts/items`: Thêm lại `sku_2` với `Quantity = 3` (kiểm tra tính năng tự dồn số lượng thành 5).
  * `DELETE /api/v1/carts/items/{itemId}`: Xóa từng món lẻ.
  * `DELETE /api/v1/carts/items?clearAll=true`: Xóa toàn bộ giỏ hàng.
* **Giao tiếp Dịch vụ Ngoại vi (External Service Calls):**
  * **Catalog Service (:8002):** Gọi `GetSkuDetailsAsync` xác minh trạng thái SKU khả dụng (`IsPurchasable == true`) và lấy giá niêm yết hiện hành.
  * **Fulfillment Service (:8005):** Gọi `CheckStockAsync` kiểm tra tồn kho vật lý tại kho người bán còn đủ đáp ứng.
* **Thao tác Database Commerce:**
  * Bảng `Carts`: Khởi tạo và cập nhật các trường `ItemCount`, `SubtotalAmount`, `LastActivityAt`.
  * Bảng `CartItems`: Thêm bản ghi với snapshot tên món, ảnh và `UnitPriceSnapshot`.
* **Kết quả trả về:**
  * Thêm món thành công: HTTP 200 OK $\rightarrow$ Giỏ có 2 món, `SubtotalAmount = 500.000đ`.
  * Đổi phân loại thành công: HTTP 200 OK $\rightarrow$ Cập nhật `UnitPriceSnapshot = 150.000đ`, `SubtotalAmount = 600.000đ`.
  * Dồn số lượng: `Quantity` tăng lên 5, không tạo thêm dòng mới.
  * Xóa sạch: HTTP 200 OK $\rightarrow$ `ItemCount = 0`, `SubtotalAmount = 0đ`.

---

### Flow 2: Khảo Giá Thanh Toán & Tính Phí Vận Chuyển (Checkout Quote)

* **Mục đích:** Tính toán chính xác tổng tiền, phí vận chuyển đa người bán qua GHN, kiểm tra số dư ví người dùng, và khóa báo giá trong Distributed Cache 15 phút.
* **Đầu vào (Request Payload):**
  * `POST /api/v1/orders/quote`:
    ```json
    {
      "cartItemIds": ["cart_item_a", "cart_item_b"],
      "shippingAddress": {
        "receiverName": "Nguyen Van A",
        "contactPhone": "0901234567",
        "detailAddress": "123 Đường Lê Lợi",
        "ward": "Phường Bến Nghé",
        "district": "Quận 1",
        "province": "TP. Hồ Chí Minh",
        "ghnDistrictId": 1442,
        "ghnWardCode": "20101"
      },
      "paymentMethod": "Wallet"
    }
    ```
* **Giao tiếp Dịch vụ Ngoại vi:**
  * **Fulfillment Service (:8005):** Gọi `CalculateShopShippingFeeAsync` cho từng Shop riêng biệt (Shop A: 30.000đ, Shop B: 30.000đ $\rightarrow$ Tổng tiền ship: 60.000đ).
  * **Wallet Service (:8008):** Gọi `GetWalletBalanceAsync(buyerId)` $\rightarrow$ Trả về số dư khả dụng: `1.500.000đ`.
* **Thao tác Database & Caching:**
  * Sinh mã `QuoteId` (UUID v4 ngẫu nhiên).
  * Lưu toàn bộ snapshot báo giá vào `IDistributedCache` với key `checkout_quote:{QuoteId}` và thời gian sống `TTL = 15 phút`.
* **Kết quả trả về (HTTP 200 OK):**
  * `quoteId`: `UUID`
  * `grandTotal`: `560.000đ` (Hàng: 500.000đ + Vận chuyển: 60.000đ)
  * `walletBalance`: `1.500.000đ` (Đủ điều kiện thanh toán qua Ví)
  * `shops`: Báo giá chi tiết phân rã theo 2 shop, tính đúng phí vận chuyển độc lập.

---

### Flow 3: Chính Sách Biến Động Giá Bất Đối Xứng (Asymmetric Price Drift Policy)

* **Mục đích:** Xử lý triệt để bài toán biến động giá từ lúc bỏ giỏ/lấy quote đến lúc bấm Đặt hàng. Bảo vệ quyền lợi người mua khi giảm giá và chống gian lận/thâm hụt khi tăng giá.
* **Kịch bản A — Phát hiện Giá Tăng (Hard Block & Auto-Refresh Snapshot):**
  * **Tình huống:** Sản phẩm `sku_1` trong quote có giá `100.000đ`. Ngay trước khi đặt hàng, Seller đổi giá trên Catalog thành `120.000đ`.
  * **Xử lý:**
    1. Khi gọi `POST /api/v1/orders`, Commerce Service quét giá thực tế qua `CatalogClient.GetSkuDetailsAsync`.
    2. Phát hiện giá tăng `120.000đ > 100.000đ`.
    3. Tự động đồng bộ cập nhật lại `UnitPriceSnapshot = 120.000đ` vào giỏ hàng `CartItems` trong DB.
    4. Chặn đứng giao dịch, trả về HTTP 409 Conflict.
  * **Kết quả trả về:**
    * HTTP Status: `409 Conflict`
    * Error Code: `PRICE_CHANGED`
    * Thông báo: *"One or more items in your cart have changed price. Please review your cart and try again."*
* **Kịch bản B — Phát hiện Giá Giảm (Auto-Apply Price Drop):**
  * **Tình huống:** Người mua lấy lại Quote mới với giá `120.000đ`. Lúc này Catalog mở khuyến mãi giảm xuống `90.000đ`.
  * **Xử lý:**
    1. Commerce Service phát hiện giá giảm `90.000đ < 120.000đ`.
    2. Hệ thống **tự động áp dụng mức giá ưu đãi mới 90.000đ** cho khách hàng, tính lại `grandTotal`.
    3. Đơn hàng tạo thành công mà không làm gián đoạn trải nghiệm của khách.
  * **Kết quả trả về (HTTP 201 Created):**
    * `status`: `"Paid"`
    * `appliedPriceDrops`:
      ```json
      [
        {
          "skuId": "sku_1",
          "productName": "Áo Thun Polo A",
          "oldPrice": 120000.0,
          "newPrice": 90000.0,
          "savedAmount": 30000.0
        }
      ]
      ```

---

### Flow 4: Tạo Đơn Hàng & Thanh Toán Ví + Trừ Kho Vĩnh Viễn

* **Mục đích:** Đặt hàng từ Quote hợp lệ, kiểm tra cơ chế chống Double-submit, trừ tiền ví, chuyển giữ kho thành trừ kho vĩnh viễn, dọn dẹp giỏ hàng.
* **Đầu vào (Request Payload):**
  * `POST /api/v1/orders` kèm `QuoteId`, `PaymentMethod: Wallet`, địa chỉ giao hàng.
* **Kịch bản thực thi:**
  1. **Atomic Pop Cache:** Đọc và xóa ngay lập tức `checkout_quote:{QuoteId}` khỏi Redis/Memory Cache. Các request đúp đến sau trong cùng mili-giây bị chặn ngay với lỗi `QUOTE_EXPIRED`.
  2. **Tạm khóa tồn kho (Hold Stock):** Gọi Fulfillment Service `ReserveStockAsync(orderId, items, ttlMinutes: 15)` $\rightarrow$ Trả về `true`.
  3. **Thanh toán Ví (Wallet Direct Payment):** Gọi Wallet Service `PayOrderAsync(orderId, totalAmount)` $\rightarrow$ Trả về `WalletPaymentResult.Success(txId)`.
  4. **Chốt kho vĩnh viễn (Commit Stock):** Gọi Fulfillment Service `CommitStockAsync(orderId)` $\rightarrow$ Trừ tồn kho vật lý vĩnh viễn.
  5. **Dọn giỏ hàng (Cart Cleanup):** Tự động xóa các `CartItems` đã mua khỏi giỏ hàng của Buyer.
  6. **Lưu Database:** Lưu `Order` (Status: `Paid`), các `SubOrder` (Status: `Paid`) với đầy đủ `WalletTransactionId`.
* **Kết quả trả về (HTTP 201 Created):**
  * `orderId`: `UUID`
  * `orderNo`: `ORD-20261102-XXXX`
  * `status`: `"Paid"`
  * `subOrderIds`: Danh sách ID đơn con tương ứng từng Shop.

---

### Flow 5: Cơ Chế Bù Trừ Sự Cố Database (Circuit Breaker & Compensation Rollback)

* **Mục đích:** Kiểm thử tình huống rủi ro nghiêm trọng nhất trong hệ thống phân tán: Kho đã giữ, Ví đã trừ tiền thành công, nhưng Database của Commerce Service bị crash, sập mạng hoặc vi phạm ràng buộc dữ liệu khi lưu (`SaveChangesAsync`).
* **Kịch bản thực thi:**
  1. Commerce Service trừ tiền Ví và giữ kho thành công.
  2. Gọi `_dbContext.SaveChangesAsync()` $\rightarrow$ Giả lập phát sinh ngoại lệ `InvalidOperationException` (PostgreSQL Connection Dropped).
  3. Khối `catch (Exception dbEx)` phát hiện lỗi lưu DB, lập tức ngắt mạch và kích hoạt chuỗi bù trừ phân tán (Distributed Compensation):
     * **Bước bồi hoàn 1 (Inventory Compensation):** Gọi Fulfillment Service `ReleaseStockAsync(orderId)` $\rightarrow$ Nhả lại toàn bộ hàng tồn kho đã giữ.
     * **Bước bồi hoàn 2 (Wallet Compensation):** Gọi Wallet Service `RefundOrderPaymentAsync` với `idempotencyKey: compensation-db-{orderId}` $\rightarrow$ Hoàn trả 100% số tiền đã trừ vào ví của Buyer.
  4. Ném lỗi chuẩn `CommerceDomainException("ORDER_CREATION_FAILED", 500)`.
* **Kết quả trả về:**
  * HTTP Status: `500 Internal Server Error`
  * Error Code: `ORDER_CREATION_FAILED`
  * Message: *"An error occurred while saving the order. All inventory allocations and payments have been safely rolled back."*
  * **Đảm bảo tính toàn vẹn (ACID Invariant):** Không có rò rỉ tồn kho (Zero inventory leak), không xảy ra tình trạng người mua bị trừ tiền oan mà không có đơn.

---

### Flow 6: Quản Lý Quá Hạn Thanh Toán Lai (Hybrid Payment Expiration)

* **Mục đích:** Tự động hủy đơn hàng quá hạn thanh toán (15 phút đối với đơn thường, 24 giờ đối với đơn Đấu giá), nhả kho và ghi nhận lịch sử hủy đơn.
* **Kịch bản kiểm thử 2 cơ chế song song:**
  1. **Passive Guard (Bảo vệ Thụ động trên API GET):**
     * Đơn hàng `PendingPayment` tạo trước đó 16 phút (`PaymentDeadlineAt < UtcNow`).
     * Khi người mua hoặc Shop gọi `GET /api/v1/orders/{orderId}`, hệ thống tự động phát hiện quá hạn, chuyển trạng thái đơn và đơn con thành `Cancelled`.
     * Tự động gọi Fulfillment Service `ReleaseStockAsync(orderId)` nhả kho ngay tại chỗ.
  2. **Active Sweeper (Quét Chủ động Background Worker):**
     * Background service `PaymentTimeoutWorker` chạy chu kỳ 60 giây một lần.
     * Quét cơ sở dữ liệu tìm các đơn `Status == PendingPayment` và `PaymentDeadlineAt < UtcNow`.
     * Ghi nhận bản ghi `OrderCancellation` với `ReasonCode = PaymentTimeout` và `ActorRole = System`.
* **Kết quả kiểm thử:**
  * Đơn quá hạn bị hủy hoàn toàn.
  * Tồn kho kho hàng được nhả về cho khách khác mua.
  * Bản ghi `OrderCancellation` được lưu trữ đầy đủ phục vụ kiểm toán (Audit).

---

### Flow 7: Shop Tiếp Nhận Đơn Con & Tự Động Sinh Vận Đơn GHN

* **Mục đích:** Người bán xác nhận tiếp nhận đơn con (`SubOrder`), hệ thống xác thực đơn đã thanh toán và tự động tích hợp đơn vị vận chuyển GHN.
* **Đầu vào (Request Payload):**
  * `POST /api/v1/orders/{orderId}/sub-orders/accept`:
    ```json
    {
      "subOrderId": "sub_order_a_id"
    }
    ```
* **Kịch bản thực thi:**
  1. Kiểm tra quyền sở hữu của Seller đối với `SubOrder` (nếu không đúng trả về HTTP 403 `FORBIDDEN`).
  2. Kiểm tra điều kiện: Đơn thanh toán Online phải ở trạng thái `Paid` (nếu còn `PendingPayment` trả về lỗi HTTP 400 `INVALID_STATUS`).
  3. Cập nhật `SubOrder.Status = Confirmed`, ghi nhận thời điểm `SellerAcceptedAt = UtcNow`.
  4. Tự động gọi Fulfillment Service (:8005) `CreateShipmentAsync(subOrderId)` để sinh mã vận đơn Giao Hàng Nhanh (GHN).
* **Kết quả trả về (HTTP 200 OK):**
  * SubOrder chuyển sang `Confirmed`.
  * Vận đơn GHN được tạo thành công kèm mã tra cứu và link in phiếu giao vận.

---

### Flow 8: Shop Từ Chối Đơn Con (Targeted Stock Release & Partial Refund)

* **Mục đích:** Người bán từ chối 1 đơn con trong đơn hàng đa người bán (ví dụ Shop A từ chối vì hết size áo, nhưng Shop B vẫn có hàng để giao).
* **Đầu vào (Request Payload):**
  * `POST /api/v1/orders/{orderId}/sub-orders/reject`:
    ```json
    {
      "subOrderId": "sub_order_a_id",
      "reasonCode": "OUT_OF_STOCK",
      "reasonText": "Shop A hết size áo này"
    }
    ```
* **Kịch bản thực thi:**
  1. Cập nhật `SubOrder_A.Status = Cancelled`.
  2. **Đơn con của Shop B giữ nguyên:** `SubOrder_B.Status` vẫn là `Paid`, quy trình giao vận Shop B không bị gián đoạn.
  3. **Giải phóng kho có mục tiêu (Targeted Release):**
     * Chỉ gọi `FulfillmentClient.ReleaseSubOrderStockAsync(subOrder_A, itemsOfShopA)`.
     * **TUYỆT ĐỐI KHÔNG** gọi nhả kho toàn đơn `ReleaseStockAsync(orderId)` (tránh lỗi hủy oan hàng của Shop B).
  4. **Ghi nhận hoàn tiền (Refund Tracking):**
     * Tự động khởi tạo bản ghi `OrderRefund` trong Database với `Status = RefundStatus.Requested`, số tiền đúng bằng giá trị đơn của Shop A (`200.000đ`), lý do `SELLER_REJECTED`.
* **Kết quả trả về (HTTP 200 OK):**
  * SubOrder A hủy thành công.
  * Hàng Shop A được trả về kho, hàng Shop B vẫn an toàn.
  * Bản ghi yêu cầu hoàn tiền được ghi nhận phục vụ đối soát tài chính.

---

## 3. DANH SÁCH BUG & LỖ HỔNG ĐÃ PHÁT HIỆN VÀ BIỆN PHÁP KHẮC PHỤC (REMEDIATION AUDIT)

Trong quá trình rà soát mã nguồn và kiểm thử End-to-End, toàn bộ 8 điểm rủi ro kỹ thuật kiến trúc đã được làm rõ và xử lý triệt để:

| Mã Lỗ Hổng / Vấn đề | Phân Loại | Vị Trí Code Ảnh Hưởng | Trạng Thái Trước | Giải Pháp Đã Áp Dụng & Kết Quả Xác Minh |
| :--- | :---: | :--- | :--- | :--- |
| **ISSUE-01: Stateless Quote Tampering** | Bảo mật / Toàn vẹn | `OrderService.cs` (`QuoteCheckoutAsync`) | Báo giá không lưu cache, phía client có thể sửa payload khi đặt hàng. | Đã triển khai `IDistributedCache` lưu Quote với TTL 15 phút. Toàn bộ tính toán lấy từ server-side cache. ✅ |
| **ISSUE-02: Double-Submit Race Condition** | Đồng quy (Concurrency) | `OrderService.cs` (`CreateOrderAsync`) | Bấm nút đặt hàng nhanh 2 lần có thể tạo 2 đơn hàng trùng lặp. | Đã áp dụng cơ chế **Atomic Pop** (`GetStringAsync` + `RemoveAsync` ngay lập tức). Request thứ hai bị loại trừ ngay. ✅ |
| **ISSUE-03: Asymmetric Price Drift** | Nghiệp vụ / Tài chính | `OrderService.cs` (lines 195-255) | Giá sản phẩm đổi trong giỏ hàng không được xử lý nhất quán. | Áp dụng chính sách **Bất đối xứng**: Giá tăng $\rightarrow$ Hard block HTTP 409 + Auto refresh snapshot; Giá giảm $\rightarrow$ Tự động áp dụng và trả về `appliedPriceDrops`. ✅ |
| **ISSUE-04: Inventory Reservation Leak** | Tồn kho (Inventory) | `OrderService.cs` (lines 398-404) | Thanh toán xong không chốt kho vĩnh viễn, hết hạn TTL hàng bị nhả ra. | Bổ sung lệnh gọi bắt buộc `FulfillmentClient.CommitStockAsync(orderId)` ngay sau khi trừ tiền thành công. ✅ |
| **ISSUE-05: Wallet Balance Visibility** | Trải nghiệm / Handoff | `OrderService.cs` (lines 135-155) | Người dùng không biết số dư ví có đủ thanh toán không ở màn hình Quote. | Tích hợp gọi `WalletClient.GetWalletBalanceAsync` trong bước Quote để trả về `walletBalance` cho Frontend. ✅ |
| **ISSUE-06: Distributed Save Failure** | Khả năng Chịu lỗi (Resilience) | `OrderService.cs` (lines 454-501) | Sập DB khi lưu đơn làm mất tiền ví và thất thoát hàng trong kho. | Xây dựng **Circuit Breaker Compensation Rollback**: Tự động giải phóng kho (`ReleaseStockAsync`) và hoàn tiền ví (`RefundOrderPaymentAsync` kèm Idempotency Key). ✅ |
| **ISSUE-07: Untargeted Stock Release** | Nghiệp vụ Đa Shop | `OrderService.cs` (`SellerRejectSubOrderAsync`) | Một Shop từ chối đơn làm nhả nhầm toàn bộ tồn kho của các Shop khác trong cùng đơn. | Thay thế bằng `ReleaseSubOrderStockAsync`, chỉ nhả danh sách SKU thuộc đơn con của đúng Shop từ chối. ✅ |
| **ISSUE-08: Order Payment Deadlock** | Quản lý Vòng đời | `OrderService.cs` & `PaymentTimeoutWorker.cs` | Đơn quá hạn thanh toán nằm treo vĩnh viễn trong DB, chiếm dụng tồn kho. | Kết hợp mô hình **Hybrid Expiration**: Passive Guard (hủy tức thời khi có request GET) + Active Sweeper (Background Worker quét mỗi 60s). Hỗ trợ đơn thường (15m) và đơn đấu giá (24h). ✅ |

---

## 4. KẾT LUẬN & KIẾN NGHỊ VẬN HÀNH (CONCLUSION & RECOMMENDATIONS)

1. **Độ sẵn sàng sản xuất (Production Readiness):**
   * Toàn bộ mã nguồn của Commerce Service đạt chuẩn Clean Architecture, tuân thủ 100% nguyên tắc No-Repository over EF Core, cấu trúc RESTful API đồng nhất.
   * Tất cả các trường hợp biên (Edge Cases), lỗi mạng bên ngoài, lỗi kết nối cơ sở dữ liệu đều có cơ chế bảo vệ và bù trừ tự động.
2. **Khuyến nghị cho các Service phụ thuộc:**
   * **Wallet Service (:8008):** Đảm bảo duy trì thời gian phản hồi dưới 2.0s cho API `/api/v1/payments` và hỗ trợ xử lý Idempotency Key cho API hoàn tiền `/api/v1/refunds`.
   * **Fulfillment Service (:8005):** Đảm bảo worker đồng bộ trạng thái giao vận GHN hoạt động ổn định để cập nhật kịp thời trạng thái `Shipping` và `Delivered` cho các `SubOrders`.

---
*Báo cáo được lập tự động bởi Senior QA Automation & Backend Engineer.*  
*Thư mục lưu trữ: `d:\CommerceService\COMMERCE_TEST_REPORT.md`*
