# Feature Specification: Default Security Hardening & Secret Key Remediation

**Feature Branch**: `033-default-security-and-key-remediation`  
**Created**: 2026-11-19  
**Status**: Ready for Review / Implementation  
**Scope**: Inward-Only (CommerceService Port 8003, Database `commerce_db:5435`)  
**Input**: Khắc phục triệt để lỗi "Bảo mật mặc định: bỏ khóa dịch vụ ghi cứng và trả 503 khi thiếu cấu hình" theo phản hồi audit từ đội FE/Security.

---

## 1. Bối cảnh & Vấn đề Kỹ thuật (Problem Statement & Context)

### 1.1. Hiện trạng phản hồi từ Audit
> *"3. **Bảo mật mặc định**: lẽ ra/đã bỏ khóa dịch vụ ghi cứng và trả 503 khi thiếu cấu hình. Dev vẫn ghi cứng khóa ở Commerce (3 file) và Fulfillment (7 file), và có mặc định `ghn-webhook-secret-key` cho webhook GHN."*

### 1.2. Nguyên lý "Bảo mật mặc định" (Security by Default / Fail-Closed)
- Trong kiến trúc Microservices phân tán, các cuộc gọi Service-to-Service (S2S) nội bộ và Webhook nhạy cảm (như cập nhật trạng thái đơn hàng từ bưu cục GHN qua Fulfillment) được bảo vệ bằng header `X-Internal-Service-Key`.
- **Quy tắc bất biến:** Mã nguồn thực thi (production code) và file cấu hình mặc định **tuyệt đối không được chứa bất kỳ secret key ghi cứng (hardcoded key) nào**.
- Khi một instance khởi chạy mà môi trường chưa cung cấp biến môi trường bảo mật (`INTERNAL_SERVICE_SECRET`), hệ thống phải thực thi cơ chế **Fail-Closed**:
  - Không được âm thầm fallback sang một secret cố định đã bị lộ.
  - Bắt buộc từ chối ngay lập tức mọi request nội bộ với mã trạng thái HTTP **503 Service Unavailable** và mã lỗi chuẩn `INTERNAL_CONFIGURATION_ERROR`.

### 1.3. Bóc tách phạm vi ranh giới (Service Boundary)
- **Thuộc phạm vi CommerceService**: 3 file thực thi đang hardcode fallback key `eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e` và 1 middleware trả sai mã lỗi HTTP 500 thay vì 503.
- **Thuộc phạm vi FulfillmentService (Nằm ngoài repo này)**: 7 file chứa hardcoded key và secret mặc định `ghn-webhook-secret-key` cho Webhook trực tiếp từ Giao Hàng Nhanh (GHN). CommerceService không tích hợp trực tiếp với GHN bưu cục nên không chứa key này.

---

## 2. Mục tiêu Kỹ thuật & Tiêu chí Nghiệm thu (Acceptance Criteria)

- [ ] **AC-01 (Loại bỏ Hardcode trong Mã nguồn C#)**:
  - Loại bỏ hoàn toàn fallback `?? "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"` tại 5 khối khởi tạo HttpClient trong `Commerce.Infrastructure/DependencyInjection.cs`.
  - Loại bỏ fallback `?? "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"` trong constructor của `Commerce.Infrastructure/Services/Auction/AuctionClient.cs`.
- [ ] **AC-02 (Làm sạch Cấu hình Mặc định & Docker Compose)**:
  - Trong `docker-compose.yml`, loại bỏ giá trị mặc định `:-eaT0Ombups...` trong khai báo `${INTERNAL_SERVICE_SECRET}`.
  - Trong `Commerce.Api/appsettings.json` và `Commerce.Api/appsettings.Development.json`, đặt giá trị `InternalSecurity:ServiceKey` thành chuỗi rỗng `""`. Chỉ lưu giữ key mẫu trong các file `.example.json` và `.example`.
- [ ] **AC-03 (Chuẩn hóa HTTP 503 cho Middleware & Guard)**:
  - `Commerce.Api/Security/InternalServiceKeyGuard.cs`: Duy trì cơ chế kiểm tra thời gian cố định `CryptographicOperations.FixedTimeEquals` và trả về HTTP 503 `INTERNAL_CONFIGURATION_ERROR` khi thiếu key cấu hình.
  - `Commerce.Api/Middleware/InternalServiceAuthMiddleware.cs`: Cập nhật dòng 42 trả về HTTP **503 Service Unavailable** (thay vì HTTP 500) khi `expectedKey` là null/whitespace.
- [ ] **AC-04 (Hành vi của Typed Clients khi Thiếu Key)**:
  - Khi `internalServiceKey` không được cấu hình (null hoặc whitespace), các client không tự động chèn header rác `X-Internal-Service-Key`.
  - Client log Warning nếu thực hiện gọi S2S nội bộ mà thiếu cấu hình key bảo mật.
- [ ] **AC-05 (Kiểm thử Tự động - Test Coverage)**:
  - Xây dựng file test mới `Commerce.UnitTests/Security/InternalServiceSecurityTests.cs` kiểm chứng 100%:
    1. Thiếu cấu hình key $\rightarrow$ `InternalServiceKeyGuard.Check` trả về HTTP 503 `INTERNAL_CONFIGURATION_ERROR`.
    2. Header `X-Internal-Service-Key` bị thiếu $\rightarrow$ trả về HTTP 401 `INTERNAL_KEY_INVALID`.
    3. Header `X-Internal-Service-Key` sai $\rightarrow$ trả về HTTP 401 `INTERNAL_KEY_INVALID`.
    4. Header `X-Internal-Service-Key` chính xác $\rightarrow$ trả về `null` (cho phép đi tiếp).
    5. `InternalServiceAuthMiddleware` khi thiếu key $\rightarrow$ trả về HTTP 503 `INTERNAL_CONFIGURATION_ERROR`.
- [ ] **AC-06 (Regression Prevention)**:
  - Toàn bộ 356+ unit test hiện có trong `Commerce.UnitTests` tiếp tục pass 100%.

---

## 3. Thiết kế Chi tiết & Thay đổi Mã nguồn (Detailed Implementation Plan)

### 3.1. `Commerce.Infrastructure/DependencyInjection.cs`
**Trước thay đổi:**
```csharp
var internalServiceKey = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
    ?? configuration["INTERNAL_SERVICE_SECRET"]
    ?? configuration["InternalSecurity:ServiceKey"]
    ?? "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e";
```
**Sau thay đổi:**
Trích xuất một hàm helper dùng chung hoặc đọc thống nhất:
```csharp
var internalServiceKey = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
    ?? configuration["INTERNAL_SERVICE_SECRET"]
    ?? configuration["InternalSecurity:ServiceKey"];

if (!string.IsNullOrWhiteSpace(internalServiceKey))
{
    client.DefaultRequestHeaders.Add("X-Internal-Service-Key", internalServiceKey);
}
```
*Áp dụng đồng bộ cho cả 5 clients:* `IWalletClient`, `ICatalogClient`, `IFulfillmentClient`, `IUserClient`, `IAuctionClient`.

---

### 3.2. `Commerce.Infrastructure/Services/Auction/AuctionClient.cs`
**Trước thay đổi:**
```csharp
_serviceKey = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
    ?? configuration["INTERNAL_SERVICE_SECRET"]
    ?? configuration["InternalSecurity:ServiceKey"]
    ?? "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e";
```
**Sau thay đổi:**
```csharp
_serviceKey = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
    ?? configuration["INTERNAL_SERVICE_SECRET"]
    ?? configuration["InternalSecurity:ServiceKey"];
```

---

### 3.3. `Commerce.Api/Middleware/InternalServiceAuthMiddleware.cs`
**Trước thay đổi (Dòng 39-45):**
```csharp
if (string.IsNullOrWhiteSpace(expectedKey))
{
    _logger.LogError("Internal service key is not configured.");
    context.Response.StatusCode = StatusCodes.Status500InternalServerError; // <-- LỖI 500
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("{\"errorCode\":\"INTERNAL_CONFIGURATION_ERROR\",\"message\":\"Internal service authentication is not configured.\"}");
    return;
}
```
**Sau thay đổi:**
```csharp
if (string.IsNullOrWhiteSpace(expectedKey))
{
    _logger.LogError("Internal service key is not configured.");
    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; // <-- CHUẨN HÓA 503
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("{\"errorCode\":\"INTERNAL_CONFIGURATION_ERROR\",\"message\":\"Internal service authentication is not configured.\"}");
    return;
}
```

---

### 3.4. `docker-compose.yml`
**Trước thay đổi (Dòng 28):**
```yaml
InternalSecurity__ServiceKey: "${INTERNAL_SERVICE_SECRET:-eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e}"
```
**Sau thay đổi:**
```yaml
InternalSecurity__ServiceKey: "${INTERNAL_SERVICE_SECRET:-}"
```

---

### 3.5. `Commerce.Api/appsettings.json` và `appsettings.Development.json`
**Trước thay đổi:**
```json
"InternalSecurity": {
  "ServiceKey": "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"
}
```
**Sau thay đổi:**
```json
"InternalSecurity": {
  "ServiceKey": ""
}
```
*(Ghi chú: Khóa mẫu tiếp tục được duy trì tại `appsettings.example.json` và `appsettings.Development.example.json` để phục vụ onboarding developer mới theo đúng chuẩn bảo mật).*

---

### 3.6. Bổ sung Test Suite: `tests/Commerce.UnitTests/Security/InternalServiceSecurityTests.cs`
Xây dựng xUnit test class độc lập kiểm chứng các nhánh logic:
1. `Check_MissingConfig_Returns503ServiceUnavailable()`
2. `Check_MissingHeader_Returns401Unauthorized()`
3. `Check_InvalidKey_Returns401Unauthorized()`
4. `Check_ValidKey_ReturnsNull()`
5. `Middleware_MissingConfig_Returns503ServiceUnavailable()`

---

## 4. Kế hoạch Thực hiện (Execution Steps)

1. **Bước 1**: Cập nhật file cấu hình môi trường (`appsettings.json`, `appsettings.Development.json`, `docker-compose.yml`).
2. **Bước 2**: Sửa mã nguồn C# tại `DependencyInjection.cs`, `AuctionClient.cs`, `InternalServiceAuthMiddleware.cs`.
3. **Bước 3**: Viết unit tests tại `tests/Commerce.UnitTests/Security/InternalServiceSecurityTests.cs`.
4. **Bước 4**: Chạy `dotnet build Commerce.sln -c Release` và `dotnet test` xác nhận 100% tests vượt qua.
5. **Bước 5**: Quét toàn bộ codebase bằng `git grep` để đảm bảo chuỗi secret hardcode không còn xuất hiện trong code thực thi.

---

## 5. Khuyến nghị Bàn giao cho FulfillmentService (Cross-Service Alignment)

Để hệ thống đồng bộ toàn diện, đội phụ trách `FulfillmentService` cần nhận checklist sau:
1. Quét tìm và loại bỏ secret key hardcode tại **7 file** tương ứng trong repo `FulfillmentService`.
2. Loại bỏ chuỗi mặc định `ghn-webhook-secret-key` trong cấu hình GHN Webhook; chuyển sang yêu cầu biến môi trường `GHN_WEBHOOK_SECRET_KEY` bắt buộc, trả HTTP 503 khi chưa được cấu hình.
3. Đồng bộ key `INTERNAL_SERVICE_SECRET` qua file `.env` hoặc hệ thống quản lý bí mật (Secrets Manager / Kubernetes Secrets) khi chạy tích hợp.
