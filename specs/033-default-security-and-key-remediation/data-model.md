# Data Model & Configuration Schemas: Default Security Hardening & Secret Key Remediation

**Feature**: `033-default-security-and-key-remediation`  
**Date**: 2026-11-19

---

## 1. Cấu hình Môi trường & Khóa Bí mật (Security Configurations)

### 1.1. Nguồn phân giải Khóa Bí mật (Resolution Precedence)
Hệ thống phân giải khóa `internalServiceKey` theo thứ tự ưu tiên giảm dần:
1. Biến môi trường: `Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")`
2. Configuration gốc: `configuration["INTERNAL_SERVICE_SECRET"]`
3. Section JSON: `configuration["InternalSecurity:ServiceKey"]`

Nếu cả 3 nguồn trên đều không tồn tại, trả về `null` hoặc whitespace $\rightarrow$ Hệ thống kích hoạt cơ chế **Fail-Closed**.

```json
{
  "InternalSecurity": {
    "ServiceKey": ""
  }
}
```

---

## 2. Mô hình Dữ liệu Lỗi Bảo mật (Security Error Envelopes)

### 2.1. Lỗi Thiếu Cấu hình Khóa Bí mật (HTTP 503 Service Unavailable)
Được trả về bởi `InternalServiceKeyGuard.Check` và `InternalServiceAuthMiddleware` khi `internalServiceKey` là null hoặc whitespace:
- **HTTP Status Code**: `503 Service Unavailable`
- **Content-Type**: `application/json`
- **Schema**:
```json
{
  "errorCode": "INTERNAL_CONFIGURATION_ERROR",
  "message": "Internal service authentication is not configured."
}
```

### 2.2. Lỗi Không Hợp lệ / Thiếu Header Xác thực (HTTP 401 Unauthorized)
Được trả về bởi `InternalServiceKeyGuard.Check` khi request không chứa header `X-Internal-Service-Key` hoặc giá trị không khớp:
- **HTTP Status Code**: `401 Unauthorized`
- **Content-Type**: `application/json`
- **Schema**:
```json
{
  "errorCode": "INTERNAL_KEY_INVALID",
  "message": "Internal service authentication is required."
}
```

### 2.3. Lỗi Từ chối Truy cập Resource Nội bộ (HTTP 403 Forbidden)
Được trả về bởi `InternalServiceAuthMiddleware` khi request vào đường dẫn `/api/v1/internal/*` mà không thỏa mãn điều kiện xác thực:
- **HTTP Status Code**: `403 Forbidden`
- **Content-Type**: `application/json`
- **Schema**:
```json
{
  "errorCode": "FORBIDDEN",
  "message": "Access denied to internal service resource."
}
```

---

## 3. Danh mục Endpoint Nội bộ Được Bảo vệ (Protected Internal Endpoints)

| Endpoint | Method | Cơ chế Bảo vệ | Hành vi khi thiếu cấu hình | Hành vi khi sai key |
| :--- | :--- | :--- | :--- | :--- |
| `/api/v1/sub-orders/{subOrderId}/delivery-status` | `POST` | `InternalServiceKeyGuard.Check` | HTTP 503 `INTERNAL_CONFIGURATION_ERROR` | HTTP 401 `INTERNAL_KEY_INVALID` |
| `/api/v1/auction-orders/settlement` | `POST` | `InternalServiceKeyGuard.Check` | HTTP 503 `INTERNAL_CONFIGURATION_ERROR` | HTTP 401 `INTERNAL_KEY_INVALID` |
| `/api/v1/internal/auction-orders/settlement` | `POST` | `InternalServiceKeyGuard.Check` | HTTP 503 `INTERNAL_CONFIGURATION_ERROR` | HTTP 401 `INTERNAL_KEY_INVALID` |
| `/api/v1/internal/orders/dispute-eligibility` | `GET` | `InternalServiceKeyGuard.Check` | HTTP 503 `INTERNAL_CONFIGURATION_ERROR` | HTTP 401 `INTERNAL_KEY_INVALID` |
| `/api/v1/internal/orders/review-eligibility` | `GET` | `InternalServiceKeyGuard.Check` | HTTP 503 `INTERNAL_CONFIGURATION_ERROR` | HTTP 401 `INTERNAL_KEY_INVALID` |
| `/api/v1/internal/*` | ALL | `InternalServiceAuthMiddleware` | HTTP 503 `INTERNAL_CONFIGURATION_ERROR` | HTTP 403 `FORBIDDEN` |
