# 05 — DANH SÁCH KIỂM CHỨNG & NGHIỆM THU (COMMERCE SERVICE CHECKLIST)
> **Trạng thái triển khai (2026-10-24):** ❌ Chưa nghiệm thu được — 0 project test, 0 bề mặt gRPC, port local lệch đặc tả. Build chưa được chạy trong lần cập nhật tài liệu này (quy định cấm chạy `dotnet`).
> **Nguồn sự thật:** code trong repo này (`file:line` ghi kèm trong từng bảng).
> **Cập nhật lần cuối:** 2026-10-24.
---
## 1. LỆNH BIÊN DỊCH & CHẠY THỬ NGHIỆM
Tên solution thật là **`Commerce.sln`** (không phải `CommerceService.sln` — xem `ls` ở thư mục repo và `Dockerfile:8`).
```bash
# 1. Biên dịch solution Commerce (4 project trong Commerce.sln)
dotnet build Commerce.sln -c Release
# 2. Khởi chạy service
#    - Port container/Docker: 8003 (Dockerfile:3-4)
#    - Port local từ launchSettings.json: http = 5105, https = 7035 (KHÔNG phải 8003)
dotnet run --project Commerce.Api
#    → http://localhost:5105/health  (endpoint duy nhất đang tồn tại, Program.cs:58)
#    → http://localhost:5105/swagger (chỉ khi ASPNETCORE_ENVIRONMENT=Development, Program.cs:35-39)
# 3. Kiểm thử qua ApiGateway: ❌ chưa thực hiện được
#    Đặc tả cũ: mở gateway.runtime.json, đổi "commerce": { "Mode": "Real", "Address": "http://localhost:8003" }
#    Thực tế: Commerce chưa có rpc nào (0/10), nên gateway không có bề mặt để gọi.
```
Ghi chú kiểm chứng có/không (đã kiểm bằng lệnh, không chạy dotnet):
- `Commerce.sln` ✅ tồn tại; `CommerceService.sln` ❌ không tồn tại.
- Project test: ❌ **không có** (`find . -name "*Tests*"` = 0; toàn repo 67 file `.cs` đều thuộc 4 project trên).
- Migration: ✅ 1 file `Commerce.Infrastructure/Migrations/20261024103145_InitialCommerceSchema.cs`. Công cụ `dotnet ef` dùng dạng global tool — **`./.tools/dotnet-ef` không tồn tại** trong repo (đường dẫn này trong README cũ là sai).
- `dotnet test Commerce.sln` sẽ **không có gì để chạy** vì chưa có test project.
---
## 2. CHECKLIST TRƯỚC KHI COMMIT CHO AI AGENTS
- [ ] **Biên dịch thành công 100%**: `dotnet build Commerce.sln -c Release`, không lỗi cảnh báo nghiêm trọng.
- [ ] **Không vi phạm rào chắn**: Không dùng Repository Pattern bọc ngoài EF Core (hiện trạng ✅: `CartService`/`OrderService` dùng `ICommerceDbContext` trực tiếp).
- [ ] **Bắt buộc Idempotency-Key**: Logic tạo đơn phải kiểm tra `Idempotency-Key` UUID — ❌ **chưa có logic tạo đơn**, cũng chưa có chỗ nào đọc header này.
- [ ] **Port & Database**: Lắng nghe đúng port `8003` khi chạy container; khi chạy local phải nhớ `launchSettings.json` đang là `5105`/`7035` (⚠️ lệch đặc tả, CO-05). DB: `commerce_db` trên port `5435`, mật khẩu `MatkhaucuaTui123@` (✅ `appsettings.json:10`).
- [ ] **Git Status**: Không commit file rác (`bin/`, `obj/`, `.idea/`, `.env`). Kiểm 2026-10-24: `git ls-files` = **0 file** thuộc `.idea/`; `.gitignore:24` đã ignore `.idea/`, `.gitignore:38` ignore `.env` — hiện trạng sạch, không có `.idea/workspace.xml` bị commit.
- [ ] **Không có bề mặt gRPC**: nếu task yêu cầu "gọi thử rpc Commerce", phải chặn lại và báo thiếu — 0/10 rpc đã triển khai (`02_API_GRPC_CONTRACTS.md`).
- [ ] **Không có event**: nếu task yêu cầu "kiểm tra Saga/consumer", phải báo thiếu — 0/3 publisher, 0/4 consumer (`03_EVENT_MESSAGING.md`).