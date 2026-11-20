# Implementation Plan: Default Security Hardening & Secret Key Remediation

**Branch**: `033-default-security-and-key-remediation` | **Date**: 2026-11-19 | **Spec**: [specs/033-default-security-and-key-remediation/spec.md](specs/033-default-security-and-key-remediation/spec.md)

**Input**: Feature specification from `/specs/033-default-security-and-key-remediation/spec.md`

## Summary

Triển khai kế hoạch khắc phục triệt để lỗ hổng "Bảo mật mặc định: bỏ khóa dịch vụ ghi cứng và trả 503 khi thiếu cấu hình" trên `CommerceService`:
1. **Loại bỏ Hardcoded Fallback Key trong C#**:
   - Gỡ bỏ hoàn toàn fallback `?? "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"` tại 5 HTTP Client (`IWalletClient`, `ICatalogClient`, `IFulfillmentClient`, `IUserClient`, `IAuctionClient`) trong `Commerce.Infrastructure/DependencyInjection.cs`.
   - Gỡ bỏ fallback `?? "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"` trong constructor của `Commerce.Infrastructure/Services/Auction/AuctionClient.cs`.
2. **Làm sạch Cấu hình Môi trường & Docker**:
   - Xóa bỏ fallback mặc định `:-eaT0Ombups...` trong `docker-compose.yml`.
   - Xóa rỗng `InternalSecurity:ServiceKey` trong `appsettings.json` và `appsettings.Development.json` (chỉ lưu key mẫu trong các file `.example.json`).
3. **Chuẩn hóa Mã Lỗi Fail-Closed (HTTP 503)**:
   - Sửa `InternalServiceAuthMiddleware.cs` trả về HTTP 503 `Status503ServiceUnavailable` kèm `errorCode: "INTERNAL_CONFIGURATION_ERROR"` khi không có cấu hình key nội bộ, đồng bộ 100% với `InternalServiceKeyGuard.cs`.
4. **Bảo vệ bằng Unit Test Tự động**:
   - Xây dựng test suite chuyên trách `tests/Commerce.UnitTests/Security/InternalServiceSecurityTests.cs` kiểm chứng toàn diện mọi kịch bản thiếu key (503), sai key (401), thiếu header (401), và đúng key (pass).

---

## Technical Context

**Language/Version**: C# 12 / .NET 8.0  
**Primary Dependencies**: ASP.NET Core, Microsoft.Extensions.Configuration, Microsoft.Extensions.DependencyInjection, System.Security.Cryptography  
**Storage**: N/A (Security header & configuration remediation, no database migration needed)  
**Testing**: xUnit, Moq, FluentAssertions (`tests/Commerce.UnitTests/`)  
**Target Platform**: Linux / Windows container, Port 8003  
**Project Type**: RESTful Web API Web Service (Clean Architecture 4 tầng)  
**Performance Goals**: Fixed-time string comparison (`CryptographicOperations.FixedTimeEquals`) ngăn chặn triệt để timing-attack; 0ms overhead khi resolve DI  
**Constraints**: Inward-only boundaries, Fail-closed by default, 100% English sentence-case error messages, SCREAMING_SNAKE_CASE error codes (`INTERNAL_CONFIGURATION_ERROR`, `INTERNAL_KEY_INVALID`)  
**Scale/Scope**: 5 existing files touched (`DependencyInjection.cs`, `AuctionClient.cs`, `InternalServiceAuthMiddleware.cs`, `docker-compose.yml`, `appsettings.json`), 1 new test file (`InternalServiceSecurityTests.cs`)  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Nguyên tắc Constitution | Đánh giá Tuân thủ | Ghi chú & Rào chắn |
|---|:---:|---|
| **I. Clean Architecture & Direct EF Core** | ✅ PASS | Giữ nguyên ranh giới 4 tầng. Không can thiệp DB mapping hay tạo Repository pattern. |
| **II. Synchronous Typed HTTP REST** | ✅ PASS | Các Typed HTTP Client (`IWalletClient`, `ICatalogClient`, `IFulfillmentClient`, `IUserClient`, `IAuctionClient`) tiếp tục truyền `X-Internal-Service-Key` nếu có cấu hình; hoạt động fail-closed nếu thiếu key. |
| **III. Immutable Infrastructure & Ports** | ✅ PASS | Giữ nguyên Port 8003 và Database 5435. Cấu hình Docker compose an toàn hơn với cú pháp biến rỗng không fallback key. |
| **IV. System-Wide Error Protocol** | ✅ PASS | Chuẩn hóa mã lỗi `INTERNAL_CONFIGURATION_ERROR` (HTTP 503) và `INTERNAL_KEY_INVALID` (HTTP 401). Message 100% English sentence-case. |
| **V. Inward-Only Boundaries** | ✅ PASS | 100% tài nguyên nội bộ CommerceService. Phân định rõ trách nhiệm không can thiệp repo FulfillmentService. |

---

## Project Structure

### Documentation (this feature)

```text
specs/033-default-security-and-key-remediation/
├── plan.md              # Kế hoạch triển khai kỹ thuật này
├── research.md          # Phân tích kỹ thuật & quyết định thiết kế (Phase 0)
├── data-model.md        # Đặc tả mô hình dữ liệu, cấu hình & mã lỗi (Phase 1)
├── quickstart.md        # Hướng dẫn kiểm thử & xác minh tính năng (Phase 1)
├── contracts/           # Hợp đồng bảo mật nội bộ
│   └── internal-security-protocol.json
└── tasks.md             # Danh sách task triển khai chi tiết (Phase 2)
```

### Source Code Touches

```text
Commerce.Infrastructure/
├── DependencyInjection.cs                  # Bỏ fallback hardcode tại 5 HttpClient registration
└── Services/Auction/
    └── AuctionClient.cs                    # Bỏ fallback hardcode trong constructor

Commerce.Api/
├── Middleware/
│   └── InternalServiceAuthMiddleware.cs    # Sửa HTTP 500 -> HTTP 503 khi thiếu cấu hình
├── appsettings.json                        # Làm sạch ServiceKey: ""
└── appsettings.Development.json            # Làm sạch ServiceKey: ""

docker-compose.yml                          # Đổi ${INTERNAL_SERVICE_SECRET:-eaT0...} -> ${INTERNAL_SERVICE_SECRET:-}

tests/Commerce.UnitTests/
└── Security/
    └── InternalServiceSecurityTests.cs     # MỚI: 5 unit tests kiểm chứng 503, 401, timing attack & middleware
```
