# COMMERCE SERVICE SPEC KIT — CHỈ DẪN KỸ THUẬT CHO AI AGENT & DEVELOPER

> **Trạng thái triển khai (2026-11-16):** ✅ ĐÃ HOÀN THIỆN & CHUẨN HÓA 100% RESTFUL — 3 Controllers (`CartsController`, `OrdersController`, `AuctionOrdersController`), Clean Architecture 4 tầng, No-Repository over EF Core, 4 Migrations PostgreSQL, 5 Outbound Typed Clients, Background Workers, và 60+ Test Cases đạt 100% Pass. Bãi bỏ hoàn toàn gRPC, RabbitMQ, MassTransit và Saga.
> **Nguồn sự thật:** Code trong `Commerce.sln` (`Commerce.Api`, `Commerce.Application`, `Commerce.Domain`, `Commerce.Infrastructure`, `tests/Commerce.UnitTests`).
> **Cập nhật lần cuối:** 2026-11-16.

Tài liệu này là **Bộ Chỉ Dẫn & Đặc Tả Kỹ Thuật (Spec Kit)** cho repository **Commerce** thuộc hệ sinh thái Microservices **Nexus**. Giúp mọi lập trình viên và AI Coding Agent (Cursor, Claude, Copilot, DeepSeek...) nắm bắt bối cảnh nghiệp vụ giỏ hàng, đơn hàng, thanh toán và **loại bỏ hoàn toàn nguy cơ ảo giác (zero hallucination)**.

---

## 1. MỤC LỤC BỘ ĐẶC TẢ COMMERCE SERVICE (SPECS/)

| File Đặc Tả | Trọng Tâm Nội Dung | Trạng thái | Bằng chứng thực tế |
|:---|:---|:---|:---|
| [00_AGENT_MISSION_GUARDRAILS.md](00_AGENT_MISSION_GUARDRAILS.md) | Bounded Context, Port `8003`, DB `commerce_db` (`5435`), điều phối REST đồng bộ, rào chắn chống ảo giác. | ✅ HOÀN THIỆN | 16 Entity, 3 Feature Services, 0 gRPC, 0 Saga |
| [01_CLEAN_ARCHITECTURE_LAYERS.md](01_CLEAN_ARCHITECTURE_LAYERS.md) | Cấu trúc 4 tầng Clean Architecture, Feature Co-location, 1-to-1 Controller Pairing, Outbound Typed Clients, Workers. | ✅ HOÀN THIỆN | 4 Projects + 1 Test Project trong `Commerce.sln` |
| [02_API_REST_CONTRACTS.md](02_API_REST_CONTRACTS.md) | Danh mục Hợp đồng RESTful API chính thức (`Carts`, `Orders`, `AuctionOrders`, `SubOrders`), RBAC, Idempotency-Key. | ✅ HOÀN THIỆN | `Commerce.Api/Controllers/Public/*Controller.cs` |
| [03_EVENT_MESSAGING.md](03_EVENT_MESSAGING.md) | Tuyên bố bãi bỏ RabbitMQ, MassTransit & Saga; thay thế bằng đồng bộ Outbound Typed REST Clients và Transactional Compensation. | ✅ HOÀN THIỆN | 0 package MassTransit, 0 queue RabbitMQ |
| [04_DATA_PERSISTENCE.md](04_DATA_PERSISTENCE.md) | Mô hình CSDL PostgreSQL `commerce_db`, 16 Entities, 4 Migrations, Soft Delete, Concurrency Tokens, Seed Data. | ✅ HOÀN THIỆN | `AppDbContext.cs`, 16 Configurations, 4 Migrations |
| [05_DEV_VERIFICATION_CHECKLIST.md](05_DEV_VERIFICATION_CHECKLIST.md) | Quy trình build, test, xác minh tính toàn vẹn hệ thống và checklist trước khi commit cho AI Agents. | ✅ HOÀN THIỆN | `Commerce.UnitTests` (60+ tests passed) |

---

## 2. THÔNG SỐ VẬN HÀNH CHÍNH

| Thông số | Giá trị chuẩn | Trạng thái | Bằng chứng |
|---|---|---|---|
| Ngôn ngữ & Runtime | .NET 8 (C# 12), `net8.0` | ✅ CÓ | 5 file `*.csproj` đều `<TargetFramework>net8.0</TargetFramework>` |
| Cổng dịch vụ | `8003` (Container & Local thống nhất) | ✅ CÓ | `Dockerfile:3-4`, `docker-compose.yml:37` |
| Bề mặt API | 100% RESTful Web API (JSON RFC 9110) | ✅ CÓ | `CartsController`, `OrdersController`, `AuctionOrdersController` |
| Cơ sở dữ liệu | PostgreSQL 16, port `5435`, DB `commerce_db` | ✅ CÓ | `appsettings.json`, `AppDbContext.cs` |
| Giao tiếp Liên dịch vụ | Synchronous Typed REST Clients (`HttpClientFactory`) | ✅ CÓ | `IWalletClient`, `ICatalogClient`, `IFulfillmentClient`, `IUserClient`, `IAuctionClient` |
| Chống click lặp | `Idempotency-Key` (UUIDv4) lưu trữ phân tán / cache | ✅ CÓ | Xử lý tại `OrderCheckout`, `AuctionOrder` |
| Kiểm thử tự động | xUnit + FluentAssertions + Mock (`tests/Commerce.UnitTests`) | ✅ CÓ | 60+ Test cases (Domain, App, Fault, Concurrency, E2E) |

---

## 3. NGUYÊN TẮC ĐỌC TÀI LIỆU DÀNH CHO AI AGENT
- Mọi quy chuẩn trong bộ `specs/00_` đến `05_` là các chuẩn mực vận hành đã được phê duyệt và đang chạy trong mã nguồn.
- Tài liệu kiến trúc toàn diện chi tiết nhất của hệ thống nằm tại `docs/COMMERCE_ARCHITECTURE_AND_FLOW_SPEC.md` (v3.0).
- Khi có bất kỳ nghi ngờ nào về luồng thực thi, hãy tham chiếu trực tiếp mã nguồn trong `Commerce.sln`.
