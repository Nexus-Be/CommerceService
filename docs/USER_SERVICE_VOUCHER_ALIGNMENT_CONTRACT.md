# USER SERVICE — VOUCHER PERMISSION GATE ALIGNMENT CONTRACT

> **Target Service**: `UserService` (Port `8001`, `user_db`)  
> **Calling Service**: `CommerceService` (Port `8003`) via `IUserClient` (`UserClient.cs`)  
> **Protocol**: Synchronous REST over HTTP/JSON  
> **Endpoint**: `GET http://user-service:8001/api/v1/internal/users/{userId}/permission-check?action=manage_voucher`  
> **Security Headers**: `X-Internal-Service-Key`, `x-user-id`, W3C `traceparent`  
> **Status**: RATIFIED & IMPLEMENTED  
> **Last Updated**: 2026-11-17  

---

## 1. Bối cảnh & Mục đích

Nhằm ngăn chặn các Shop có hành vi gian lận (tạo mã khống, tài khoản đang chịu chế tài phạt hoặc bị đình chỉ hoạt động) tạo và chỉnh sửa các chương trình khuyến mãi, `CommerceService` bổ sung chốt kiểm soát động (**Dynamic Permission Gate**) trước mọi tác vụ quản trị voucher:
- **Tạo mới Voucher Shop / Sản phẩm**: `POST /api/v1/discounts`
- **Chỉnh sửa thông tin Voucher**: `PUT /api/v1/discounts/{id}`
- **Bật/Tắt kích hoạt Voucher**: `PATCH /api/v1/discounts/{id}/status`
- **Xóa Voucher**: `DELETE /api/v1/discounts/{id}`

`CommerceService` không tự phán đoán trạng thái kỷ luật của Seller mà sẽ gọi sang `UserService` để thẩm định quyền `manage_voucher`.

---

## 2. Đặc tả Hợp đồng API (`action=manage_voucher`)

### 2.1. Yêu cầu HTTP (Request)
- **Method**: `GET`
- **URL**: `/api/v1/internal/users/{sellerId}/permission-check?action=manage_voucher`
- **Headers**:
  ```http
  X-Internal-Service-Key: <secret_internal_key>
  x-user-id: <seller_guid>
  traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
  ```

---

### 2.2. Quy tắc Nghiệp vụ Thẩm định tại `UserService`

Khi nhận được request với tham số `action=manage_voucher`, `UserService` thực hiện kiểm tra:
1. **Kiểm tra trạng thái tài khoản**: Tài khoản phải ở trạng thái hoạt động bình thường (`UserStatus.Active`). Nếu tài khoản bị khóa (`Banned`), tạm khóa (`Suspended`) $\rightarrow$ Từ chối.
2. **Kiểm tra vai trò (Role)**: Người dùng phải sở hữu vai trò `Seller` hoặc `Admin`/`Staff`.
3. **Kiểm tra các hạn chế / chế tài đang có hiệu lực (Active Restrictions)**:
   - Nếu Seller đang bị gắn cờ hạn chế thuộc các nhóm:
     - `Seller:PromotionSuspension` (Tạm đình chỉ quyền khuyến mãi do spam/lừa đảo).
     - `Account:BanAccount` (Khóa tài khoản toàn phần).
     - `Seller:PenaltyPointsExceeded` (Điểm phạt vi phạm vượt ngưỡng cho phép).
   $\rightarrow$ Trả về `isAllowed = false`.
4. **Nếu thỏa mãn tất cả**: Trả về `isAllowed = true`.

---

### 2.3. Cấu trúc Phản hồi (Responses)

#### A. Trường hợp Hợp lệ (Cho phép quản lý Voucher)
- **HTTP Status**: `200 OK`
- **JSON Body**:
  ```json
  {
    "isAllowed": true,
    "errorCode": null,
    "message": null
  }
  ```

#### B. Trường hợp Bị giới hạn (Từ chối)
- **HTTP Status**: `200 OK` (hoặc `403 Forbidden` kèm ProblemDetails)
- **JSON Body**:
  ```json
  {
    "isAllowed": false,
    "errorCode": "SELLER_ACTION_RESTRICTED",
    "message": "Your seller account is restricted from managing promotion vouchers."
  }
  ```

---

## 3. Hành vi Xử lý Phía `CommerceService`

1. **Khi nhận `isAllowed: true`**: `CommerceService` tiếp tục tiến trình tạo/sửa voucher bình thường.
2. **Khi nhận `isAllowed: false`**: `CommerceService` lập tức ngắt giao dịch và ném ngoại lệ `CommerceDomainException`:
   - HTTP Status: `403 Forbidden`
   - Error Code: `SELLER_ACTION_RESTRICTED` (hoặc mã lỗi chi tiết từ UserService gửi sang)
   - Message: Trả về nguyên văn thông báo từ UserService cho người bán biết lý do bị chặn.
3. **Khi `UserService` bị sập hoặc Timeout quá 10 giây (Fail-Closed Policy)**:
   - `CommerceService` kích hoạt cơ chế bảo vệ **Fail-Closed**: Nghiêm cấm tạo voucher khi chưa xác thực được quyền để đảm bảo an toàn hệ thống, trả về `503 USER_SERVICE_UNAVAILABLE`.

---

## 4. Checklist Kỹ thuật dành cho User Service Team

- [ ] Đăng ký thêm xử lý cho giá trị tham số `action=manage_voucher` tại controller/service thẩm định quyền nội bộ.
- [ ] Đảm bảo endpoint nội bộ kiểm tra `X-Internal-Service-Key`.
- [ ] Thời gian phản hồi API duy trì $< 50$ms để không ảnh hưởng đến trải nghiệm của Seller trên giao diện quản trị.
