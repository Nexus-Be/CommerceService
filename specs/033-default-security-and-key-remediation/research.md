# Research & Design Decisions: Default Security Hardening & Secret Key Remediation

**Feature**: `033-default-security-and-key-remediation`  
**Date**: 2026-11-19  
**Status**: Approved

---

## 1. Nghiên cứu & Quyết định Kỹ thuật (Technical Research & Decisions)

### Quyết định 1: Cơ chế phân giải Internal Service Key trong `DependencyInjection.cs`
- **Vấn đề**: Hiện tại chuỗi đọc key được copy-paste lặp lại 5 lần tại các khối đăng ký HttpClient (`IWalletClient`, `ICatalogClient`, `IFulfillmentClient`, `IUserClient`, `IAuctionClient`) kèm fallback ghi cứng `"eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"`.
- **Giải pháp xem xét**:
  - *Phương án A*: Giữ nguyên 5 chỗ đọc và chỉ xóa chuỗi fallback `?? "eaT0Ombups..."`.
  - *Phương án B*: Trích xuất hàm `private static string? ResolveInternalServiceKey(IConfiguration configuration)` dùng chung cho cả 5 client và xóa bỏ hoàn toàn fallback.
- **Quyết định chọn**: **Phương án B**.
- **Lý do**:
  - DRY (Don't Repeat Yourself), đảm bảo nếu sau này có bổ sung thêm biến môi trường hoặc nguồn nạp bí mật thì chỉ cần sửa tại một điểm duy nhất.
  - Loại bỏ hoàn toàn nguy cơ sót fallback key ở bất kỳ client nào.

---

### Quyết định 2: Ứng xử của Outbound Typed Clients khi thiếu cấu hình Key
- **Vấn đề**: Khi biến môi trường `INTERNAL_SERVICE_SECRET` không được cấp, client outbound nên ném lỗi ngay lúc khởi động DI (`InvalidOperationException`) hay cho phép khởi chạy nhưng không đính kèm header?
- **Giải pháp xem xét**:
  - *Phương án A*: Ném lỗi ngay khi Start ứng dụng nếu thiếu key.
  - *Phương án B*: Nếu `internalServiceKey` là null hoặc whitespace $\rightarrow$ Không đính kèm header `X-Internal-Service-Key`. Khi gọi sang downstream service (như Wallet hoặc Fulfillment), downstream service sẽ tự động từ chối với HTTP 503 hoặc 401 theo đúng nguyên tắc fail-closed.
- **Quyết định chọn**: **Phương án B**.
- **Lý do**:
  - Cho phép chạy các Unit Tests / Mocking cục bộ mà không bắt buộc phải fake cấu hình key ở mọi test fixture.
  - Đảm bảo tính linh hoạt: Việc từ chối là trách nhiệm của lớp bảo mật (Guard / Middleware), client không nên tự động nhét dữ liệu rác hoặc làm crash application lifecycle khi chưa phát sinh request.

---

### Quyết định 3: Đồng bộ mã lỗi HTTP 503 giữa Guard và Middleware
- **Vấn đề**: 
  - `InternalServiceKeyGuard.cs` đã tuân thủ đúng quy chuẩn: Khi thiếu key cấu hình $\rightarrow$ trả về HTTP 503 `Status503ServiceUnavailable` kèm `errorCode: "INTERNAL_CONFIGURATION_ERROR"`.
  - Tuy nhiên `InternalServiceAuthMiddleware.cs` (dòng 42) lại trả về HTTP 500 `Status500InternalServerError`.
- **Giải pháp xem xét**:
  - Giữ 500 hay chuyển sang 503?
- **Quyết định chọn**: Chuyển sang HTTP 503 `Status503ServiceUnavailable`.
- **Lý do**:
  - Lỗi thiếu cấu hình dịch vụ nội bộ thuộc nhóm `Service Unavailable` (hệ thống chưa sẵn sàng phục vụ các luồng phụ thuộc do thiếu điều kiện hạ tầng), không phải là unhandled crash/bug nội bộ của runtime (HTTP 500).
  - Đáp ứng 100% tiêu chí audit của FE: *"bỏ khóa dịch vụ ghi cứng và trả 503 khi thiếu cấu hình"*.

---

### Quyết định 4: Nâng cấp bảo mật chống Timing Attack cho `InternalServiceAuthMiddleware`
- **Vấn đề**: Trong `InternalServiceAuthMiddleware.cs`, dòng 57 đang so sánh chuỗi bằng `string.Equals(providedKey.ToString(), expectedKey, StringComparison.Ordinal)`. Cách so sánh này có nguy cơ bị tấn công dò tìm thời gian (Side-channel timing attack).
- **Quyết định chọn**: Thay thế bằng `CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(providedKey.ToString()), Encoding.UTF8.GetBytes(expectedKey))`.
- **Lý do**:
  - Đồng nhất 100% với cách xử lý an toàn trong `InternalServiceKeyGuard.cs`.
  - Thời gian so sánh là hằng số bất kể key đúng bao nhiêu ký tự đầu tiên, ngăn chặn hoàn toàn việc đo đạc độ trễ phản hồi mạng để đoán key.

---

### Quyết định 5: Quản trị cấu hình trong `appsettings` và `docker-compose`
- **Vấn đề**: File `docker-compose.yml` có `InternalSecurity__ServiceKey: "${INTERNAL_SERVICE_SECRET:-eaT0Ombups...}"` và `appsettings.json` đang lưu thẳng key.
- **Quyết định chọn**:
  - `docker-compose.yml`: Sửa thành `InternalSecurity__ServiceKey: "${INTERNAL_SERVICE_SECRET:-}"`.
  - `appsettings.json` và `appsettings.Development.json`: Để rỗng `"ServiceKey": ""`.
  - Giữ key mẫu trong `appsettings.example.json` và `.env.example` phục vụ việc tham khảo khi setup môi trường dev mới.
- **Lý do**:
  - Đảm bảo repo git sạch hoàn toàn, không lưu trữ bí mật môi trường thật.
  - Bắt buộc developer hoặc DevOps phải cung cấp biến môi trường tường minh khi vận hành.
