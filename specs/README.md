# COMMERCE SERVICE SPEC KIT — CHỈ DẪN KỸ THUẬT CHO AI AGENT & DEVELOPER
> **Trạng thái triển khai (2026-10-24):** ❌ Chưa có bề mặt dịch vụ — 16/16 entity + 1 migration ✅, nhưng 0/10 rpc gRPC, 0/3 event publish, 0/4 consumer, 0 project test, 3 hàm đọc.
> **Nguồn sự thật:** code trong repo này (`file:line` ghi kèm trong từng bảng).
> **Cập nhật lần cuối:** 2026-10-24.
Tài liệu này là **Bộ Chỉ Dẫn & Đặc Tả Kỹ Thuật (Spec Kit)** cho repository **Commerce** thuộc hệ sinh thái Microservices **Nexus**. Giúp mọi lập trình viên và AI Coding Agent (Cursor, Claude, Copilot, DeepSeek...) nắm bắt bối cảnh nghiệp vụ giỏ hàng, đơn hàng, điều phối Checkout Saga và **loại bỏ hoàn toàn nguy cơ ảo giác (zero hallucination)**.
---
## 1. MỤC LỤC BỘ ĐẶC TẢ COMMERCE SERVICE (SPECS/)
| File Đặc Tả | Trọng Tâm Nội Dung | Trạng thái | Bằng chứng |
|:---|:---|:---|:---|
| [00_AGENT_MISSION_GUARDRAILS.md](00_AGENT_MISSION_GUARDRAILS.md) | Bounded Context, Port `8003` (container) / `5105`–`7035` (local), DB `commerce_db` (`5435`), các điều cấm kỵ cốt lõi. | ⚠️ MỘT PHẦN | 16 entity ✅; 0 hàm `CreateOrder`, 0 Saga, 0 gRPC |
| [01_CLEAN_ARCHITECTURE_LAYERS.md](01_CLEAN_ARCHITECTURE_LAYERS.md) | Cấu trúc 4 tầng Clean Architecture (`.Api`, `.Application`, `.Domain`, `.Infrastructure`), luồng phụ thuộc. | ⚠️ MỘT PHẦN | 4/4 project ✅ (`Commerce.sln`); không CQRS, không Saga, không domain method |
| [02_API_GRPC_CONTRACTS.md](02_API_GRPC_CONTRACTS.md) | Hợp đồng gRPC `CommerceApi` (đặc tả), giỏ hàng, đơn hàng, Idempotency-Key. | ❌ CHƯA CÓ | 0/10 rpc; 0 file `.proto`; không package gRPC |
| [03_EVENT_MESSAGING.md](03_EVENT_MESSAGING.md) | Điều phối Checkout Saga State Machine qua MassTransit **8.3.0** và RabbitMQ. | ❌ CHƯA CÓ (nghiệp vụ) | hạ tầng + EF Outbox ✅ (`Commerce.Infrastructure/DependencyInjection.cs:23-48`); 0 publisher, 0 consumer |
| [04_DATA_PERSISTENCE.md](04_DATA_PERSISTENCE.md) | Thực thể `Cart`, `CartItem`, `Order`, `SubOrder`, `SubOrderItem`, DbContext EF Core 8 (No Repository). | ✅ CÓ (schema) | 16 `DbSet` ↔ 16 bảng `dbml/service/commerce.dbml`; 1 migration |
| [05_DEV_VERIFICATION_CHECKLIST.md](05_DEV_VERIFICATION_CHECKLIST.md) | Lệnh biên dịch, kiểm thử tự động, checklist kiểm tra trước khi commit. | ❌ CHƯA NGHIỆM THU ĐƯỢC | 0 test project; chưa có bề mặt để test |
---
## 2. THÔNG SỐ VẬN HÀNH CHÍNH
| Thông số | Giá trị thật | Trạng thái | Bằng chứng |
|---|---|---|---|
| Ngôn ngữ & Runtime | .NET 8 (C# 12), `net8.0` | ✅ CÓ | 4 file `*.csproj` đều `<TargetFramework>net8.0</TargetFramework>` |
| Cổng container/Docker | `8003` | ✅ CÓ | `Dockerfile:3-4`, `docker-compose.yml:37` |
| Cổng chạy local | `http = 5105`, `https = 7035` (iisExpress `5157`/`44374`) | ⚠️ LỆCH ĐẶC TẢ | `Commerce.Api/Properties/launchSettings.json:17,27` |
| REST surface | chỉ `GET /health` (+ Swagger ở Development) | ⚠️ MỘT PHẦN | `Commerce.Api/Program.cs:35-39,58` |
| Cơ sở dữ liệu | PostgreSQL, port `5435`, DB `commerce_db`, mật khẩu `MatkhaucuaTui123@` | ✅ CÓ | `Commerce.Api/appsettings.json:10`; `Commerce.Infrastructure/DependencyInjection.cs:14-15` |
| Message broker | RabbitMQ `5672` (guest/…), vhost `/` | ⚠️ CHỈ HẠ TẦNG | `Commerce.Infrastructure/DependencyInjection.cs:35-44` |
| MassTransit | **8.3.0** (không phải 8.4.1) | ✅ CÓ | `Commerce.Infrastructure/Commerce.Infrastructure.csproj:22-24` |
| Idempotency-Key (UUID) chống click đúp | Chưa có ở bất kỳ đâu | ❌ CHƯA CÓ | grep `Idempotency` = 0 |
| OpenTelemetry | Có config chết `Otel:Endpoint`, không có package/không đọc | ❌ CHƯA CÓ | `Commerce.Api/appsettings.json:18-20`; grep `Otel\|OpenTelemetry` trong `.cs` = 0 |
---
## 3. NGUYÊN TẮC ĐỌC TÀI LIỆU
- Mọi tuyên bố kỹ thuật trong bộ specs này mang nhãn ✅ CÓ / ⚠️ MỘT PHẦN / ❌ CHƯA CÓ.
- Nhãn ❌ nghĩa là **đặc tả mục tiêu, chưa triển khai** — không được trích dẫn như thể hệ thống đã có.
- Khi tài liệu và code khác nhau, **code là sự thật**; hãy đọc file được trích trong cột "Bằng chứng" trước khi tin phần mô tả.