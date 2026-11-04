# HỢP ĐỒNG TÍCH HỢP REST API & ĐỐI CHIẾU MÃ NGUỒN CHUẨN HÓA
## COMMERCE SERVICE ↔ FULFILLMENT SERVICE (THE GOLDEN STANDARD)

> **Dự án:** Nexus Microservices Ecosystem  
> **Service gửi yêu cầu:** `CommerceService` (Port `8003`, DB `commerce_db:5435`)  
> **Service đối chiếu:** `FulfillmentService` (Port `8006`, DB `fulfillment_db`)  
> **Người tiếp nhận:** Anh Ngô Nhựt Minh — Fulfillment Service Lead  
> **Phiên bản:** `v1.3.0-ALIGNED` (Đã chuẩn hóa 100% khớp Báo cáo Nghiệm thu E2E Task F01-F08 của Fulfillment)  
> **Ngày lập:** 2026-11-04  
> **Trạng thái:** ✅ **THỐNG NHẤT 100% THEO CHUẨN RESTFUL INTERNAL (THE GOLDEN STANDARD)**  

---

## 1. LỜI MỞ ĐẦU & NGUYÊN TẮC THỐNG NHẤT

Sau khi đại diện kỹ thuật của CommerceService đối chiếu trực tiếp với mã nguồn C# trên nhánh **`dev`** của FulfillmentService (`D:\FulfillmentService`), hai bên đã thống nhất chuẩn hóa toàn bộ luồng giao tiếp liên dịch vụ theo **Phương án 3 (The Golden Standard)** nhằm:
1. **Khớp 100% với Controller & Middleware hiện có của Fulfillment**: Không làm vỡ code đã viết của Fulfillment, giúp hệ thống chạy thông suốt ngay lập tức.
2. **Triệt tiêu bão Request (Zero HTTP on Timeout)**: Phân định rạch ròi trách nhiệm tự chủ dữ liệu của từng service, loại bỏ các cuộc gọi HTTP thừa thãi gây nghẽn mạng I/O.

---

## 2. CHI TIẾT ĐỒNG BỘ CÁC ENDPOINT ĐÃ THỐNG NHẤT

### 2.1. Kiểm tra tồn kho lẻ (`GET /api/v1/inventories/{skuId}`)
* **Trạng thái:** ✅ Khớp 100%.
* **Phía Commerce:** Gọi `GET api/v1/inventories/{skuId}`, đọc trường `availableQuantity` để xác định tồn kho thực tế.

---

### 2.2. Kiểm tra tồn kho hàng loạt (`POST /api/v1/inventories/by-skus`)
* **Trạng thái:** ✅ Đã đồng bộ Model Binding.
* **Quy cách:** Phía Commerce cam kết gửi **Mảng JSON trần (Raw Array)** để khớp trực tiếp với `[FromBody] IReadOnlyList<Guid> skuIds` của Fulfillment:
  ```json
  [
    "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
    "e3a1f802-5c8e-4b69-8d62-7f91c98a5e12"
  ]
  ```
* **Khuyến nghị cho Fulfillment (Tương lai):** Fulfillment có thể hỗ trợ thêm DTO bọc `{ "skuIds": [...] }` để sẵn sàng mở rộng thêm tham số (`warehouseId`, `storeId`) mà không làm vỡ hợp đồng cũ.

---

### 2.3. Khóa giữ tồn kho tạm thời (`POST /api/v1/internal/inventories/reservations`)
* **Trạng thái:** ✅ Khớp 100% với `InternalInventoryReservationsController`.
* **Header bắt buộc:** `X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`.
* **Payload Commerce gửi sang:**
  ```json
  {
    "referenceId": "b1a9e7bc-6b84-46d5-9e17-092347640841",
    "holdMinutes": 15,
    "items": [
      {
        "skuId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
        "quantity": 2
      }
    ]
  }
  ```
* **Xử lý thiếu hàng:** Fulfillment ném `InsufficientInventoryBatchException`, trả về HTTP 400 ProblemDetails (`errorCode: "INSUFFICIENT_INVENTORY"`). Commerce lập tức thông báo rõ ràng cho Buyer và rollback đơn hàng.

---

### 2.4. Trừ kho vĩnh viễn (`POST /api/v1/internal/inventories/reservations/{referenceId}/commit`)
* **Trạng thái:** ✅ Khớp 100%.
* **Quy cách:** 
  - Không truyền Request Body (Payload = 0 bytes).
  - Truyền mã đơn hàng (`orderId`) vào `{referenceId}` trên URL Path.
  - Header: `X-Internal-Service-Key`.
* **Cam kết Safe Idempotency:** Nếu mạng chập chờn gọi lại lần 2 cho đơn đã trừ kho, Fulfillment trả về `200 OK` an toàn, không ném 500 và không trừ lặp trong `inventory_ledger`.

---

### 2.5. Nhả kho khi Buyer bấm "Hủy đơn" (`POST /api/v1/internal/inventories/reservations/{referenceId}/release`)
* **Trạng thái:** ✅ Khớp 100%.
* **Quy cách:**
  - Không truyền Request Body.
  - URL: `POST /api/v1/internal/inventories/reservations/{orderId}/release?reason=OrderCancelled`.
  - Header: `X-Internal-Service-Key`.
* **Mục đích:** Chỉ kích hoạt khi khách hàng **chủ động bấm Hủy đơn trên App** lúc đơn chưa hết hạn 15 phút.

---

### 2.6. Nhả kho từng phần khi Shop từ chối SubOrder (`POST /api/v1/internal/inventories/reservations/{referenceId}/release-items`)
* **Trạng thái:** ✅ Khớp 100% (Chuẩn hóa theo Task F04 Fulfillment).
* **Quy cách:**
  - URL: `POST /api/v1/internal/inventories/reservations/{orderId}/release-items`.
  - Header: `X-Internal-Service-Key`.
  - Body:
    ```json
    {
      "items": [
        { "skuId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "quantity": 2 }
      ],
      "reason": "SubOrderRejected"
    }
    ```
* **Mục đích:** Chỉ nhả kho các mặt hàng của SubOrder bị từ chối, bảo lưu trọn vẹn tồn kho của các shop khác trong cùng Master Order.

---

### 2.7. Báo giá phí vận chuyển động (`POST /api/v1/internal/shipping/quote`)
* **Trạng thái:** ✅ Khớp 100% (Chuẩn hóa theo Task F06 Fulfillment).
* **Quy cách:**
  - URL: `POST /api/v1/internal/shipping/quote`.
  - Header: `X-Internal-Service-Key`.
  - Body: `ShopShippingQuoteRequest` (SellerId, GhnDistrictId, GhnWardCode, Items).
  - Trả về: `ShippingFeeResult` (`feeAmount`).

---

### 2.8. Tạo vận đơn giao hàng khi Shop tiếp nhận đơn (`POST /api/v1/internal/shipments`)
* **Trạng thái:** ✅ Khớp 100% (Chuẩn hóa theo Task F07 Fulfillment).
* **Quy cách:**
  - URL: `POST /api/v1/internal/shipments`.
  - Header: `X-Internal-Service-Key`.
  - Body: `{ "subOrderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6" }`.
  - Trả về: `ShipmentCreationResult` (`trackingCode`, `carrierCode`, `labelPrintUrl`).
  - Lũy đẳng: Nếu SubOrder đã có vận đơn thì trả về bản ghi cũ, không tạo vận đơn trùng trên GHN.

---

## 3. THỐNG NHẤT QUY TẮC: BÃI BỎ LỆNH GỌI NHẢ KHO KHI HẾT HẠN 15 PHÚT

Đây là thỏa thuận kiến trúc quan trọng nhất giữa 2 bên:

1. **Fulfillment tự động nhả kho (Zero-HTTP call):**
   - Fulfillment đã triển khai hàm lõi `AutoExpireReservationsAsync` tích hợp vào Background Service định kỳ (quét mỗi 60 giây).
   - Cứ mỗi phiếu `InventoryReservation` có `expiresAt < UtcNow`, Fulfillment **tự động hoàn trả `reserved_quantity` về `available_quantity`**.
2. **Commerce tự động hủy đơn:**
   - `PaymentTimeoutWorker` của Commerce quét đơn quá hạn 15 phút và **tự động chuyển trạng thái Order sang `Cancelled` trong DB nội bộ**.
3. **Kết luận:**
   - Khi hết hạn thanh toán, **CommerceService KHÔNG CẦN gọi bất kỳ API nào sang FulfillmentService**.
   - Hai hệ thống tự dọn dẹp dữ liệu của chính mình độc lập 100%, loại bỏ hoàn toàn nguy cơ nghẽn mạng I/O hoặc sập dây chuyền!

---

## 4. ĐỒNG BỘ TRẠNG THÁI GIAO HÀNG (DELIVERY STATUS CALLBACK)

Khi bên Fulfillment nhận Webhook từ GHN báo đơn đã giao thành công:
* **Fulfillment gọi sang Commerce tại:** `POST /api/v1/sub-orders/{subOrderId}/delivery-status`
* **Header bắt buộc:** `X-Internal-Service-Key: eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e`
* **Payload:**
  ```json
  {
    "status": "Delivered",
    "deliveredAt": "2026-11-03T16:00:00Z"
  }
  ```
* Phía Commerce cam kết xử lý cập nhật trạng thái đơn con sang `Delivered` và hoàn tất quy trình giao vận.

---

## 5. KÝ TÊN & PHÊ DUYỆT HỢP ĐỒNG KỸ THUẬT

| Đại diện Commerce Service | Đại diện Fulfillment Service |
|:---:|:---:|
| *(Chữ ký & Họ tên)* | *(Chữ ký & Họ tên)* |
| **Trạng thái:** `Đã căn chỉnh 100% theo dev` | **Trạng thái:** `✅ ĐÃ PHÊ DUYỆT & ĐỒNG THUẬN 100%` |
| **Đại diện:** Team Commerce Service | **Đại diện:** Ngô Nhựt Minh — Fulfillment Lead |
| **Ngày:** 2026-11-03 | **Ngày:** 2026-11-03 |
