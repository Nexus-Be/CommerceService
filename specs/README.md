# COMMERCE SERVICE SPEC KIT — CHỈ DẪN KỸ THUẬT CHO AI AGENT & DEVELOPER

Tài liệu này là **Bộ Chỉ Dẫn & Đặc Tả Kỹ Thuật (Spec Kit)** cho repository **CommerceService** thuộc hệ sinh thái Microservices **Nexus**. Giúp mọi lập trình viên và AI Coding Agent (Cursor, Claude, Copilot, DeepSeek...) nắm bắt bối cảnh nghiệp vụ giỏ hàng, đơn hàng, điều phối Checkout Saga và **loại bỏ hoàn toàn nguy cơ ảo giác (zero hallucination)**.

---

## 1. MỤC LỤC BỘ ĐẶC TẢ COMMERCE SERVICE (SPECS/)

| File Đặc Tả | Trọng Tâm Nội Dung |
|:---|:---|
| [00_AGENT_MISSION_GUARDRAILS.md](00_AGENT_MISSION_GUARDRAILS.md) | Bounded Context, Port `8003`, DB `commerce_db` (`5435`), các điều cấm kỵ cốt lõi. |
| [01_CLEAN_ARCHITECTURE_LAYERS.md](01_CLEAN_ARCHITECTURE_LAYERS.md) | Cấu trúc 4 tầng Clean Architecture (`.Api`, `.Application`, `.Domain`, `.Infrastructure`), luồng phụ thuộc. |
| [02_API_GRPC_CONTRACTS.md](02_API_GRPC_CONTRACTS.md) | Hợp đồng gRPC `CommerceApi` trong `shop.proto`, giỏ hàng, đơn hàng, Idempotency-Key. |
| [03_EVENT_MESSAGING.md](03_EVENT_MESSAGING.md) | Điều phối Checkout Saga State Machine qua MassTransit 8.4.1 và RabbitMQ. |
| [04_DATA_PERSISTENCE.md](04_DATA_PERSISTENCE.md) | Thực thể `Cart`, `CartItem`, `Order`, `OrderLine`, `SagaState`, DbContext EF Core 8 (No Repository). |
| [05_DEV_VERIFICATION_CHECKLIST.md](05_DEV_VERIFICATION_CHECKLIST.md) | Lệnh biên dịch, kiểm thử tự động, checklist kiểm tra trước khi commit. |

---

## 2. THÔNG SỐ VẬN HÀNH CHÍNH

- **Ngôn ngữ & Runtime**: .NET 8 (C# 12).
- **Cổng gRPC Server**: Port `8003` (Không có cổng HTTP REST riêng, mọi REST call đều qua ApiGateway Port `8000`).
- **Cơ Sở Dữ Liệu**: PostgreSQL (Port `5435`, Database: `commerce_db`, Mật khẩu: `MatkhaucuaTui123@`).
- **Message Broker**: RabbitMQ (Port `5672`, Virtual Host: `/`).
- **Quy Tắc Đặt Hàng An Toàn**: Bắt buộc phải có `Idempotency-Key` (UUID) trên header để chống click đúp đặt đơn trùng.
