# Tasks: Default Security Hardening & Secret Key Remediation

**Input**: Design documents from `/specs/033-default-security-and-key-remediation/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`  
**Branch**: `033-default-security-and-key-remediation`

---

## Phase 1: Setup & Codebase Baseline

**Purpose**: Xác lập trạng thái baseline trước khi can thiệp mã nguồn.

- [X] T001 Rà soát các vị trí chứa hardcoded key trong mã nguồn thực thi qua `git grep -n "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Chuẩn bị thư viện bảo mật và cấu trúc kiểm thử trước khi sửa đổi cấu hình và client.

- [X] T002 Tạo thư mục và khung file test bảo mật tại `tests/Commerce.UnitTests/Security/InternalServiceSecurityTests.cs`

---

## Phase 3: User Story 1 - Loại bỏ Khóa Dịch vụ Ghi cứng trong Mã nguồn C# (Priority: P1) 🎯 MVP

**Goal**: Loại bỏ hoàn toàn chuỗi bí mật fallback ghi cứng `eaT0Ombups...` khỏi tất cả Typed HttpClients và AuctionClient, vận hành theo cơ chế fail-closed.

**Independent Test**: Khởi tạo `DependencyInjection` và `AuctionClient` với cấu hình rỗng, kiểm tra các client không tự động chèn key hardcode và không bị ném ngoại lệ runtime khi khởi tạo.

### Implementation for User Story 1
- [X] T003 [P] [US1] Trích xuất hàm `ResolveInternalServiceKey(IConfiguration configuration)` và loại bỏ fallback hardcode tại 5 HTTP Client (`IWalletClient`, `ICatalogClient`, `IFulfillmentClient`, `IUserClient`, `IAuctionClient`) trong `Commerce.Infrastructure/DependencyInjection.cs`
- [X] T004 [P] [US1] Loại bỏ chuỗi secret fallback hardcode trong constructor của `Commerce.Infrastructure/Services/Auction/AuctionClient.cs`

**Checkpoint**: Mã nguồn C# trong tầng `Commerce.Infrastructure` hoàn toàn sạch chuỗi secret ghi cứng.

---

## Phase 4: User Story 2 - Làm sạch Cấu hình Mặc định & Docker Compose (Priority: P1)

**Goal**: Đảm bảo các file cấu hình `appsettings.json`, `appsettings.Development.json` và `docker-compose.yml` không chứa giá trị mặc định là key bị lộ.

**Independent Test**: Kiểm tra file JSON và YAML không còn chứa key `eaT0Ombups...`, container docker khởi chạy chỉ nhận secret từ biến môi trường ngoài.

### Implementation for User Story 2
- [X] T005 [P] [US2] Xóa bỏ giá trị fallback mặc định `:-eaT0Ombups...` trong định nghĩa `InternalSecurity__ServiceKey` tại `docker-compose.yml`
- [X] T006 [P] [US2] Đặt giá trị rỗng `""` cho `InternalSecurity:ServiceKey` trong `Commerce.Api/appsettings.json` và `Commerce.Api/appsettings.Development.json`

**Checkpoint**: File cấu hình dự án không còn lưu trữ bí mật môi trường thực tế.

---

## Phase 5: User Story 3 - Chuẩn hóa Mã Lỗi HTTP 503 & Chống Timing Attack trong Middleware (Priority: P1)

**Goal**: Đồng bộ hóa mã lỗi khi thiếu cấu hình key nội bộ thành HTTP 503 `Status503ServiceUnavailable` và sử dụng `FixedTimeEquals` chống side-channel timing attack.

**Independent Test**: Gửi request tới route `/api/v1/internal/*` khi chưa cấu hình key $\rightarrow$ nhận mã lỗi HTTP 503 `INTERNAL_CONFIGURATION_ERROR`.

### Implementation for User Story 3
- [X] T007 [US3] Sửa `InternalServiceAuthMiddleware.cs` trả về HTTP 503 `Status503ServiceUnavailable` thay vì 500 khi `expectedKey` là null hoặc whitespace trong `Commerce.Api/Middleware/InternalServiceAuthMiddleware.cs`
- [X] T008 [US3] Nâng cấp phép so sánh chuỗi khóa xác thực sang `CryptographicOperations.FixedTimeEquals` trong `Commerce.Api/Middleware/InternalServiceAuthMiddleware.cs`

**Checkpoint**: Middleware nội bộ đạt tính nhất quán 100% với `InternalServiceKeyGuard.cs` và tiêu chuẩn bảo mật.

---

## Phase 6: User Story 4 - Bộ Kiểm thử Tự động Bảo vệ (Priority: P1)

**Goal**: Xây dựng bộ Unit Test tự động bao phủ 100% các kịch bản của `InternalServiceKeyGuard` và `InternalServiceAuthMiddleware` nhằm ngăn chặn hồi quy bảo mật.

**Independent Test**: Chạy `dotnet test --filter "FullyQualifiedName~InternalServiceSecurityTests"` đạt 100% Passed.

### Tests for User Story 4
- [X] T009 [P] [US4] Viết các unit test cho `InternalServiceKeyGuard.Check` (kịch bản thiếu config trả 503, thiếu header trả 401, sai key trả 401, đúng key trả null) trong `tests/Commerce.UnitTests/Security/InternalServiceSecurityTests.cs`
- [X] T010 [P] [US4] Viết các unit test cho `InternalServiceAuthMiddleware` (kịch bản thiếu config trả 503 `INTERNAL_CONFIGURATION_ERROR`, sai key trả 403 `FORBIDDEN`) trong `tests/Commerce.UnitTests/Security/InternalServiceSecurityTests.cs`

**Checkpoint**: Toàn bộ các quy tắc bảo mật mặc định đều được kiểm chứng tự động.

---

## Phase 7: Polish & Verification

**Purpose**: Xác minh toàn diện tính toàn vẹn của mã nguồn và dự án.

- [X] T011 [P] Thực thi lệnh `git grep -n "eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e"` để chứng minh production code không còn chứa secret key hardcoded
- [X] T012 Biên dịch toàn bộ solution với cấu hình Release qua `dotnet build Commerce.sln -c Release`
- [X] T013 Thực thi toàn bộ test suite dự án qua `dotnet test` xác nhận 360+ tests passed và 0 regressions

---

## Dependencies & Execution Order

### Phase Dependencies
1. **Phase 1 (Setup)**: Không phụ thuộc - thực hiện ngay.
2. **Phase 2 (Foundational)**: Phụ thuộc Phase 1 - tạo thư mục test.
3. **Phase 3 (User Story 1)** & **Phase 4 (User Story 2)** & **Phase 5 (User Story 3)**: Có thể triển khai song song hoặc tuần tự.
4. **Phase 6 (User Story 4)**: Triển khai ngay sau các story để kiểm chứng logic.
5. **Phase 7 (Polish & Verification)**: Chạy sau khi hoàn thành tất cả các tasks code và test.

### Parallel Opportunities
- T003 (`DependencyInjection.cs`) và T004 (`AuctionClient.cs`) có thể sửa đổi song song.
- T005 (`docker-compose.yml`) và T006 (`appsettings.json`) có thể sửa đổi song song.
- T009 (Guard tests) và T010 (Middleware tests) có thể viết song song.

---

## Implementation Strategy
- **MVP Increment (US1 + US2 + US3)**: Sửa dứt điểm 3 file bị FE bắt lỗi (`DependencyInjection.cs`, `AuctionClient.cs`, `docker-compose.yml`/`appsettings.json`) và chuẩn hóa HTTP 503 trong `InternalServiceAuthMiddleware.cs`.
- **Test Hardening (US4)**: Thêm bài test tự động để bảo đảm vĩnh viễn không bị hồi quy.
- **Verification**: Quét regex/grep và chạy toàn bộ unit test suite.
