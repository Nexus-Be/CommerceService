# Quickstart & Verification Guide: Default Security Hardening & Secret Key Remediation

**Feature**: `033-default-security-and-key-remediation`  
**Date**: 2026-11-19

---

## 1. Mục đích
Tài liệu hướng dẫn quy trình xác minh tính năng bảo mật mặc định (Fail-Closed) và kiểm tra làm sạch hoàn toàn các chuỗi secret key ghi cứng trong `CommerceService`.

---

## 2. Kịch bản Kiểm chứng Tự động (Automated Verification)

### 2.1. Kiểm tra không còn Secret Key ghi cứng trong Code thực thi
Chạy lệnh kiểm tra trong shell để rà soát toàn bộ dự án:
```powershell
git grep -n "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"
```
**Kỳ vọng:**
- Không còn xuất hiện trong bất kỳ file C# thực thi nào (`DependencyInjection.cs`, `AuctionClient.cs`).
- Không xuất hiện trong `docker-compose.yml`, `appsettings.json`, `appsettings.Development.json`.
- Chỉ còn xuất hiện trong các file mẫu: `.env.example`, `appsettings.example.json`, `appsettings.Development.example.json` và các file tài liệu specs lịch sử.

---

### 2.2. Kiểm thử Bộ Unit Test Bảo mật mới
Chạy riêng test suite `InternalServiceSecurityTests`:
```powershell
dotnet test --filter "FullyQualifiedName~InternalServiceSecurityTests"
```
**Kỳ vọng:**
- Toàn bộ 5 test case đều PASS:
  1. `Guard_WhenKeyNotConfigured_Returns503ServiceUnavailable`: Trả HTTP 503 và `INTERNAL_CONFIGURATION_ERROR`.
  2. `Guard_WhenHeaderMissing_Returns401Unauthorized`: Trả HTTP 401 và `INTERNAL_KEY_INVALID`.
  3. `Guard_WhenHeaderWrong_Returns401Unauthorized`: Trả HTTP 401 và `INTERNAL_KEY_INVALID`.
  4. `Guard_WhenHeaderValid_ReturnsNullAllowingExecution`: Trả `null`, cho phép request đi tiếp.
  5. `Middleware_WhenKeyNotConfigured_Returns503ServiceUnavailable`: Middleware trả HTTP 503.

---

### 2.3. Kiểm thử Toàn bộ Hệ thống (Regression Test Suite)
Chạy toàn bộ unit test của CommerceService:
```powershell
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj
```
**Kỳ vọng:**
- 360+ tests passed, 0 failed.

---

## 3. Kịch bản Kiểm thử API Thủ công (Manual API Simulation)

### Kịch bản 1: Gọi Webhook khi Server KHÔNG có cấu hình Key (Fail-Closed)
- **Thiết lập:** Khởi động CommerceService không có biến môi trường `INTERNAL_SERVICE_SECRET`.
- **Yêu cầu:**
  ```http
  POST http://localhost:8003/api/v1/sub-orders/00000000-0000-0000-0000-000000000001/delivery-status
  Content-Type: application/json
  X-Internal-Service-Key: any-key

  {
    "status": "Delivered"
  }
  ```
- **Phản hồi kỳ vọng:**
  ```http
  HTTP/1.1 503 Service Unavailable
  Content-Type: application/json

  {
    "errorCode": "INTERNAL_CONFIGURATION_ERROR",
    "message": "Internal service authentication is not configured."
  }
  ```

---

### Kịch bản 2: Gọi Webhook khi Server CÓ cấu hình Key nhưng gửi Sai Key
- **Thiết lập:** Server có `INTERNAL_SERVICE_SECRET=MySuperSecretKey`.
- **Yêu cầu:**
  ```http
  POST http://localhost:8003/api/v1/sub-orders/00000000-0000-0000-0000-000000000001/delivery-status
  Content-Type: application/json
  X-Internal-Service-Key: WrongKey

  {
    "status": "Delivered"
  }
  ```
- **Phản hồi kỳ vọng:**
  ```http
  HTTP/1.1 401 Unauthorized
  Content-Type: application/json

  {
    "errorCode": "INTERNAL_KEY_INVALID",
    "message": "Internal service authentication is required."
  }
  ```
