# LUỒNG 6: NGHIỆP VỤ QUẢN TRỊ SÀN & ĐIỀU HÀNH (ADMIN OPERATIONS GUIDE)

> **Dành cho:** Admin Portal & Staff Dashboard  
> **Base Routes:** `/api/v1/admin/platform-fee`, `/api/v1/admin/buyer-refusal-penalties`, `/api/v1/admin/refund-fund/cases`, `/api/v1/admin/orders`, `/api/v1/admin/sub-orders`  
> **Xác thực:** Bắt buộc đăng nhập với role `admin` hoặc `staff`  

---

## 1. TỔNG QUAN & MA TRẬN PHÂN QUYỀN NỘI BỘ

Các tính năng quản trị sàn phục vụ bộ phận Vận hành, Kế toán và Chăm sóc khách hàng:
- **`staff` (Nhân viên vận hành):** Được tra cứu danh sách đơn toàn sàn, xem % phí sàn, lập đề xuất hoàn tiền từ quỹ bảo hiểm sàn, xem danh sách chờ duyệt.
- **`admin` (Quản trị viên cấp cao):** Có toàn quyền như staff, đồng thời nắm quyền quyết định: cập nhật % phí sàn, thu hồi án phạt boom hàng của người mua, duyệt hoàn tiền từ quỹ bảo hiểm sàn (bao gồm cơ chế duyệt 2 cấp đối với số tiền lớn).

---

## 2. CHI TIẾT CÁC NGHIỆP VỤ QUẢN TRỊ

### 2.1. Quản Lý Tỷ Lệ Phí Sàn Hoa Hồng (Platform Fee)
- **Xem % Phí sàn hiện tại:** `GET /api/v1/admin/platform-fee`
  - **Quyền:** `staff`, `admin`
  - **Response (200 OK):**
  ```json
  {
    "defaultPercent": 5.0,
    "updatedByRole": "admin",
    "updatedAt": "2026-11-20T10:00:00Z"
  }
  ```
- **Cập nhật % Phí sàn:** `PUT /api/v1/admin/platform-fee`
  - **Quyền:** `staff`, `admin`
  - **Mô tả:** Thiết lập % phí hoa hồng sàn áp dụng cho các đơn hàng tạo sau thời điểm cập nhật (từ `0.0%` đến `100.0%`).
  - **Request Body:**
  ```json
  {
    "defaultPercent": 6.5
  }
  ```
  - **Response (200 OK):** Trả về `PlatformFeeResponse` đã cập nhật.

---

### 2.2. Thu Hồi Điểm Phạt Boom Hàng của Người Mua (Buyer Refusal Penalties)
- **Method / Path:** `POST /api/v1/admin/buyer-refusal-penalties/{subOrderId}/revoke`
- **Quyền:** `admin` (Chỉ Admin mới có quyền đổi điểm uy tín; Staff bị chặn với HTTP 403)
- **Mô tả:** Khi bưu tá báo người mua bom hàng khiến người mua bị trừ điểm uy tín tại UserService, nhưng sau đó người mua khiếu nại thành công (do lỗi bưu tá không gọi), Admin gọi API này để phục hồi điểm uy tín cho người mua.
- **Request Body:**
```json
{
  "reason": "Bưu tá không liên hệ khách hàng theo biên bản đối soát GHN"
}
```
- **Response Thành Công (200 OK - UserService đã xác nhận ngay):**
```json
{
  "subOrderId": "99998888-7777-6666-5555-444433332222",
  "confirmed": true,
  "status": "Revoked",
  "reason": "Bưu tá không liên hệ khách hàng theo biên bản đối soát GHN",
  "revokedAt": "2026-11-27T10:30:00Z"
}
```
- **Response Chờ Xử Lý (202 Accepted - UserService đang nghẽn mạng, worker đang thử lại):**
```json
{
  "subOrderId": "99998888-7777-6666-5555-444433332222",
  "confirmed": false,
  "status": "RevokePending",
  "reason": "Bưu tá không liên hệ khách hàng theo biên bản đối soát GHN",
  "revokedAt": "2026-11-27T10:30:00Z"
}
```
*Hướng dẫn FE:* Nếu nhận HTTP `202 Accepted`, hiển thị nhãn "Đang xử lý thu hồi điểm", không báo lỗi.

---

### 2.3. Bồi Hoàn Từ Quỹ Bảo Hiểm Sàn (Platform Refund Fund Cases)
Áp dụng khi đơn hàng đã thanh toán xong cho Người bán (`Completed`), nhưng sau đó phát hiện hàng giả hoặc sai sót nghiêm trọng và Sàn quyết định trích quỹ bảo hiểm để bồi hoàn cho người mua.

1. **Danh sách các ca yêu cầu bồi hoàn quỹ:** `GET /api/v1/admin/refund-fund/cases`
   - **Quyền:** `staff`, `admin`
   - **Query Params:** `status` (`AwaitingSecondApproval`, `Pending`, `Refunded`, `NeedsFinance`, `Resolved`), `page`, `pageSize`.
2. **Admin thứ nhất Đề xuất bồi hoàn quỹ:** `POST /api/v1/admin/refund-fund/cases`
   - **Quyền:** `admin`
   - **Request Body:**
   ```json
   {
     "subOrderId": "99998888-7777-6666-5555-444433332222",
     "reason": "Phát hiện hàng giả sau khi tiền đã giải ngân cho shop",
     "reportId": "33334444-5555-6666-7777-888899990000"
   }
   ```
   - *Lưu ý nghiệp vụ:* Nếu số tiền bồi hoàn vượt ngưỡng quy định, ca này sẽ chuyển sang trạng thái `AwaitingSecondApproval` và bắt buộc cần một Admin thứ 2 khác duyệt.
3. **Admin thứ hai Phê duyệt (Dual-Approval):** `POST /api/v1/admin/refund-fund/cases/{subOrderId}/approve`
   - **Quyền:** `admin` (phải khác Admin thứ nhất).
4. **Thử lại giải ngân ví:** `POST /api/v1/admin/refund-fund/cases/{subOrderId}/retry`
   - Dùng khi ví quỹ bảo hiểm hết tiền hoặc lỗi mạng (`NeedsFinance`).
5. **Đóng ca xử lý ngoài hệ thống:** `POST /api/v1/admin/refund-fund/cases/{subOrderId}/resolve`
   - Dùng khi bộ phận tài chính đã chuyển khoản thủ công cho khách.
   ```json
   {
     "note": "Đã chuyển khoản trực tiếp qua ngân hàng Vietcombank"
   }
   ```

---

### 2.4. Tra Cứu Đơn Hàng Toàn Sàn (Market-Wide Queries)

#### A. Danh sách Đơn hàng cha toàn sàn
- **Method / Path:** `GET /api/v1/admin/orders`
- **Quyền:** `staff`, `admin`
- **Query Params:**
  - `buyerId`: Lọc theo người mua.
  - `sellerId`: Lọc theo người bán.
  - `status`: Lọc theo trạng thái đơn cha (`PendingPayment`, `Paid`, `Cancelled`, `Completed`).
  - `fromDate`, `toDate`: Khoảng thời gian tạo đơn (ISO-8601).
  - `page`, `pageSize`: Phân trang.
- **Response (200 OK):** Trả về danh sách đơn hàng cha toàn sàn dạng `PagedData<OrderSummaryResponse>`.

#### B. Danh sách Đơn hàng con toàn sàn
- **Method / Path:** `GET /api/v1/admin/sub-orders`
- **Quyền:** `staff`, `admin`
- **Query Params:**
  - `sellerId`: Lọc theo Shop cụ thể.
  - `buyerId`: Lọc theo Buyer cụ thể.
  - `status`: Lọc theo trạng thái (`PendingPayment`, `Paid`, `Confirmed`, `Shipped`, `Delivered`, `Completed`, `Cancelled`, `Refunded`, `Returning`).
  - `trackingCode`: Tìm chính xác theo mã vận đơn GHN.
  - `fromDate`, `toDate`: Khoảng thời gian.
  - `page`, `pageSize`: Phân trang.
- **Response (200 OK):** Trả về danh sách đơn con toàn sàn dạng `PagedData<SubOrderSummaryResponse>`.
