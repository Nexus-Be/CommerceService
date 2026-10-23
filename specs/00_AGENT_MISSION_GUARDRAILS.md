# 00 — NHIỆM VỤ CỐT LÕI & RÀO CHẮN CHỐNG ẢO GIÁC (COMMERCE SERVICE)

---

## 1. NHIỆM VỤ & RANH GIỚI MIỀN (BOUNDED CONTEXT)

**CommerceService** chịu trách nhiệm độc quyền về việc quản lý giỏ hàng và vòng đời đơn hàng thương mại điện tử:
- Quản lý giỏ hàng (`Cart`, `CartItem`): Thêm, bớt, cập nhật số lượng, xóa sản phẩm, xóa trắng giỏ.
- Tạo đơn hàng (`CreateOrder`): Hỗ trợ cả 2 hình thức: Mua ngay (truyền danh sách `Items`) hoặc Checkout toàn bộ giỏ (truyền `CartId`).
- Quản lý vòng đời đơn hàng: `Pending` $\rightarrow$ `StockReserved` $\rightarrow$ `Paid` $\rightarrow$ `Preparing` $\rightarrow$ `Shipped` $\rightarrow$ `Completed` (hoặc `Cancelled`).
- **Đầu Mối Điều Phối Checkout Saga**: Quản lý máy trạng thái (Saga State Machine) kết nối các bước giữ kho ảo (`CatalogService`) và trừ tiền ví (`WalletService`).
- Cung cấp dịch vụ gRPC Server tại cổng `8003` cho ApiGateway.

---

## 2. NHỮNG ĐIỀU CẤM KỴ ĐỐI VỚI AI AGENTS (ANTI-HALLUCINATION GUARDRAILS)

1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**:
   - Sử dụng trực tiếp `CommerceDbContext` và `DbSet<T>` trong tầng Application.
   - Không được tạo `IOrderRepository`, `ICartRepository`, `UnitOfWork`.

2. **KHÔNG TỰ Ý TRỪ TIỀN HOẶC TRỪ KHO BÊN TRONG COMMERCE**:
   - `CommerceService` **KHÔNG** sở hữu kho hàng và **KHÔNG** sở hữu số dư tiền của khách.
   - Việc kiểm tra/giữ kho do `CatalogService` xử lý. Việc trừ số dư do `WalletService` xử lý. Commerce chỉ đóng vai trò gửi Request/Event và chờ phản hồi từ 2 service kia qua Saga.

3. **BẮT BUỘC KIỂM TRA IDEMPOTENCY-KEY KHI TẠO ĐƠN**:
   - Mọi request tạo đơn bắt buộc mang `Idempotency-Key` (UUID). Nếu trùng key trong vòng 24h, phải trả về đơn hàng đã tạo trước đó hoặc báo lỗi xung đột, không được tạo 2 đơn trùng lặp.

4. **TUYỆT ĐỐI KHÔNG THAY ĐỔI CỔNG 8003 VÀ DATABASE 5435**:
   - Service gRPC chạy tại `http://localhost:8003`.
   - Chuỗi kết nối DB trỏ tới `localhost:5435/commerce_db` với mật khẩu mặc định `MatkhaucuaTui123@`.
