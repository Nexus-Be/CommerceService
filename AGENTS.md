<!-- gitnexus:start -->
# GitNexus — Code Intelligence
This project is indexed by GitNexus as **CommerceService** (636 symbols, 799 relationships, 7 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.
> Index stale? Run `node .gitnexus/run.cjs analyze` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? `npx gitnexus analyze` (npm 11 crash → `npm i -g gitnexus`; #1939).
## Always Do
- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows. For regression review, compare against the default branch: `detect_changes({scope: "compare", base_ref: "main"})`.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `query({search_query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `context({name: "symbolName"})`.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).
## Never Do
- NEVER edit a function, class, or method without first running `impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit changes without running `detect_changes()` to check affected scope.
## Resources
| Resource | Use for |
|----------|---------|
| `gitnexus://repo/CommerceService/context` | Codebase overview, check index freshness |
| `gitnexus://repo/CommerceService/clusters` | All functional areas |
| `gitnexus://repo/CommerceService/processes` | All execution flows |
| `gitnexus://repo/CommerceService/process/{name}` | Step-by-step execution trace |
## CLI
| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |
<!-- gitnexus:end -->
---
# QUY CHUẨN VẬN HÀNH DÀNH CHO AI CODING AGENTS (COMMERCE SERVICE)
> **Trạng thái triển khai (2026-10-24):** ❌ Chưa có bề mặt dịch vụ — 16/16 bảng có entity ✅, 3 hàm đọc, 0/10 rpc gRPC, 0/3 event publish, 0/4 consumer, 0 project test.
> **Nguồn sự thật:** code trong repo này (`file:line` ghi kèm trong từng bảng).
> **Cập nhật lần cuối:** 2026-10-24.
---
## 🛑 NGUYÊN TẮC 1: BẮT BUỘC LẬP KẾ HOẠCH TRƯỚC KHI HÀNH ĐỘNG (PLAN-FIRST RULE)
Mọi AI Agent khi nhận nhiệm vụ kỹ thuật tại CommerceService **BẮT BUỘC PHẢI LẬP VÀ TRÌNH BÀY PLAN** trước khi thực hiện bất kỳ hành động sửa đổi mã nguồn, thêm/xóa file, refactor, tạo migration database hay thay đổi cấu hình nào.
Một Plan chuẩn bắt buộc phải có:
1. **Mục tiêu (Objective)**: Tóm tắt chính xác điều cần làm.
2. **Context & Files cần đọc**: Liệt kê các file nội bộ của CommerceService cần đọc theo đúng ma trận đọc file tại Nguyên tắc 3.
3. **Phân tích rủi ro & Phạm vi ảnh hưởng (Impact Analysis)**:
   - Dùng GitNexus `impact` đánh giá blast radius các symbol bị ảnh hưởng.
   - Cảnh báo rõ nếu mức độ rủi ro là HIGH hoặc CRITICAL.
4. **Checklist các bước thực hiện (Step-by-Step Implementation)**: Từng bước logic, chi tiết.
5. **Kế hoạch kiểm chứng (Verification)**: `dotnet build` và `dotnet test` tương ứng.
---
## 🧭 NGUYÊN TẮC 2: QUY TẮC PHẠM VI NỘI BỘ (INWARD-ONLY / SELF-CONTAINED)
CommerceService là một Git Repository độc lập (`CommerceService.git`):
- **TUYỆT ĐỐI CHỈ ĐƯỢC trỏ tới các tài nguyên nội bộ của CommerceService**.
- **TUYỆT ĐỐI KHÔNG ĐƯỢC trỏ ngược ra ngoài Root** (`../../../docs/...`) hay sang các Service khác (`../../Catalog/...`).
- Toàn bộ tài liệu, hướng dẫn và cấu hình trong repo này phải 100% tự cung tự cấp (Self-contained).
---
## 📚 NGUYÊN TẮC 3: DANH MỤC FILE NỘI BỘ BẮT BUỘC PHẢI ĐỌC TẠI COMMERCE SERVICE
Khi bắt đầu làm việc tại CommerceService, AI Agent **BẮT BUỘC PHẢI ĐỌC** các file nội bộ sau theo thứ tự:
1. `specs/00_AGENT_MISSION_GUARDRAILS.md`: Bounded context giỏ hàng, đơn hàng, Saga Orchestrator, port `8003`, DB `5435` (`commerce_db`).
2. `specs/01_CLEAN_ARCHITECTURE_LAYERS.md`: Cấu trúc 4 tầng Clean Architecture (⚠️ MassTransit State Machine **chưa** triển khai — chỉ có hạ tầng Outbox).
3. `specs/02_API_GRPC_CONTRACTS.md`: Hợp đồng gRPC Server (`CommerceApi`: Carts, Orders, Checkout).
4. `specs/03_EVENT_MESSAGING.md`: Điều phối Checkout Saga (`OrderCreatedEvent`, `OrderCancelledEvent`...).
5. `specs/04_DATA_PERSISTENCE.md`: Schema PostgreSQL — 16 bảng thật (`Carts`, `CartItems`, `Orders`, `SubOrders`, `SubOrderItems`, `DiscountCodes`...). ❌ Không có `OrderItems`/`OrderSagaState`; tên thật là `SubOrderItems`, và bảng Saga không tồn tại.
6. `specs/05_DEV_VERIFICATION_CHECKLIST.md`: Lệnh build & test kiểm chứng service.
---
## 🔒 NGUYÊN TẮC 4: RÀO CHẮN BẤT BIẾN TẠI COMMERCE SERVICE
1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**: Sử dụng trực tiếp `CommerceDbContext` và `DbSet<T>` (hoặc interface `ICommerceDbContext`) trong tầng Application. Cấm tạo `IOrderRepository`, `ICartRepository`, `UnitOfWork`.
2. **ĐIỀU PHỐI GIAO DỊCH PHÂN TÁN QUA MASS TRANSIT SAGA STATE MACHINE**: Mọi giao dịch Checkout đa bước phải đi qua Saga, đảm bảo bù trừ tự động khi thanh toán hoặc giữ kho thất bại.
   - ❌ **Hiện chưa triển khai**: 0 `MassTransitStateMachine<>`, 0 publisher, 0 consumer; chỉ có hạ tầng MassTransit 8.3.0 + EF Outbox trong `Commerce.Infrastructure/DependencyInjection.cs:23-48`. Đây là đặc tả bắt buộc cho code mới, không phải hiện trạng.
3. **CẤU HÌNH PORT 8003 VÀ DATABASE 5435 LÀ BẤT BIẾN**:
   - `8003` áp dụng cho container (`Dockerfile:3-4`). ⚠️ Chạy local, `Commerce.Api/Properties/launchSettings.json:17,27` đang dùng `5105`/`7035` — sai lệch đã biết, chưa sửa (chỉ được sửa `.md` trong đợt tài liệu này).
   - DB: port `5435`, database `commerce_db` (`Commerce.Api/appsettings.json:10`).
4. **CẤM SỬ DỤNG gRPC CHO CÁC DỊCH VỤ ĐỒNG BỘ - BẮT BUỘC DÙNG 100% RESTFUL API**:
   - Toàn bộ giao tiếp đồng bộ giữa API Gateway và CommerceService (hoặc giữa các microservices) bắt buộc phải sử dụng chuẩn **RESTful Web API** (JSON qua HTTP/1.1 hoặc HTTP/2).
   - Tuyệt đối **CẤM** triển khai hoặc sử dụng gRPC Server/Client cho các dịch vụ đồng bộ trong toàn bộ hệ thống.
   - Bề mặt API chính thức của CommerceService là REST `/api/v1/commerce/*`. Cấm tạo hoặc sử dụng các file Protobuf (`.proto`).
5. **CẤM TỰ Ý SỬA ĐỔI BIẾN MÔI TRƯỜNG, SECRET VÀ KEY BÍ MẬT (MANDATORY HUMAN-IN-THE-LOOP FOR SECRETS)**:
   - **Tuyệt đối CẤM mọi AI Coding Agent tự ý sửa đổi, thay thế, xóa hoặc ghi đè các biến môi trường (`.env`, `.env.*`), Connection Strings, API Keys, JWT Secrets, chứng chỉ hoặc bất kỳ thông tin bí mật (credentials/secrets) nào trong toàn bộ hệ thống.**
   - **Bắt buộc phải xin ý kiến và nhận được sự đồng ý rõ ràng (explicit approval) từ người dùng trước khi thực hiện bất kỳ thay đổi nào liên quan đến biến môi trường hoặc tài khoản bí mật.**
   - Khi phát hiện cấu hình môi trường, connection string hoặc secret key có vấn đề hoặc cần cập nhật, Agent **CHỈ ĐƯỢC PHÉP báo cáo hiện trạng, đề xuất phương án và chờ người dùng xác nhận**, tuyệt đối không tự ý can thiệp.
---
## 💬 NGUYÊN TẮC 5: QUY CHUẨN THÔNG BÁO LỖI TOÀN HỆ THỐNG (MANDATORY ERROR RULES)
Tất cả các thông báo lỗi (Error Messages, Exceptions, ProblemDetails, Validation Errors, Response Details) **BẮT BUỘC** phải tuân theo 3 nguyên tắc sau:
1. **Ngôn ngữ (100% English)**: Toàn bộ thông báo lỗi phải được viết bằng tiếng Anh. Tuyệt đối không dùng tiếng Việt trong chuỗi thông báo lỗi trả về cho client.
2. **Quy cách viết hoa (Sentence case)**: Viết hoa duy nhất chữ cái đầu câu, các từ tiếp theo viết thường (ngoại trừ tên riêng, từ viết tắt hoặc định danh kỹ thuật như `ID`, `OTP`, `JWT`, `URL`, `UUID`, `Email`...).
   - ✅ Đúng: `"Cart item not found in the user cart."`
   - ✅ Đúng: `"Order cannot be cancelled in its current status."`
   - ❌ Sai: `"Giỏ hàng không hợp lệ"` (Không dùng tiếng Việt)
   - ❌ Sai: `"order not found"` (Chữ cái đầu không viết hoa)
   - ❌ Sai: `"ORDER NOT FOUND"` (Cấm viết hoa toàn bộ)
   - ❌ Sai: `"Order Not Found Provided"` (Không dùng Title Case)
3. **Mã lỗi (Error Codes — SCREAMING_SNAKE_CASE)**: Mã lỗi luôn viết hoa toàn bộ và nối bằng dấu gạch dưới:
   - Ví dụ: `CART_NOT_FOUND`, `ORDER_NOT_FOUND`, `ORDER_INVALID_STATUS`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`.
---
## 🎯 NGUYÊN TẮC 6: ĐIỀU KIỆN HOÀN THÀNH NHIỆM VỤ (DEFINITION OF DONE)
Một task tại CommerceService chỉ hoàn tất khi:
- [ ] Đã lập Plan chi tiết trước khi thực hiện.
- [ ] Không chứa bất kỳ liên kết trỏ ngược ra ngoài CommerceService.
- [ ] `dotnet build Commerce.sln -c Release` thành công 100% (0 error). ⚠️ Tên file thật là `Commerce.sln` (`CommerceService.sln` **không tồn tại**); `Dockerfile:8` cũng dùng `Commerce.sln`.
- [ ] `dotnet test Commerce.sln -c Release` vượt qua 100% các bài test. ❌ **Hiện chưa có project test nào** (Commerce.sln chỉ có 4 project: Domain/Application/Infrastructure/Api) ⇒ lệnh này chưa kiểm chứng được gì; tiêu chí chỉ đạt sau khi thêm test project.
- [ ] Trạng thái Git sạch, không commit file rác (`bin/`, `obj/`, `.vs/`, `.idea/`). Kiểm 2026-10-24: `.gitignore:24` đã ignore `.idea/`, `git ls-files` = 0 file `.idea`. Không có `.idea/workspace.xml` bị commit.