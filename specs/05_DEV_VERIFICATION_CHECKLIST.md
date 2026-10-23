# 05 — DANH SÁCH KIỂM CHỨNG & NGHIỆM THU (COMMERCE SERVICE CHECKLIST)

---

## 1. LỆNH BIÊN DỊCH & CHẠY THỬ NGHIỆM

```bash
# 1. Biên dịch solution Commerce
dotnet build Commerce.sln -c Release

# 2. Khởi chạy service (gRPC port 8003)
dotnet run --project Commerce.Api

# 3. Thử nghiệm qua ApiGateway:
# Mở gateway.runtime.json, đổi "commerce": { "Mode": "Real", "Address": "http://localhost:8003" }
# Gọi GET /gateway/api/v1/commerce/cart trên Swagger Gateway xem kết quả thật trả về.
```

---

## 2. CHECKLIST TRƯỚC KHI COMMIT CHO AI AGENTS

- [ ] **Biên dịch thành công 100%**: Không lỗi cảnh báo nghiêm trọng.
- [ ] **Không vi phạm rào chắn**: Không dùng Repository Pattern bọc ngoài EF Core.
- [ ] **Bắt buộc Idempotency-Key**: Logic tạo đơn phải kiểm tra IdempotencyKey UUID.
- [ ] **Port & Database**: Lắng nghe đúng port `8003`, kết nối `commerce_db` trên port `5435` (Password: `MatkhaucuaTui123@`).
- [ ] **Git Status**: Không commit file rác (`bin/`, `obj/`, `.idea/`, `.env`).
