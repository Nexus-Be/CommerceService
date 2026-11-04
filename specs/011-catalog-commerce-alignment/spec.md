# Feature Specification: Catalog and Commerce Contract Alignment

**Feature Directory**: `specs/011-catalog-commerce-alignment`  
**Created**: 2026-11-03  
**Status**: Draft  
**Input**: Phê duyệt triển khai đồng bộ hóa 100% hợp đồng tích hợp giữa CatalogService (Port 8002) và CommerceService (Port 8003) dựa trên tài liệu đặc tả `srs-catalog-commerce-alignment.md`: loại bỏ hoàn toàn giá mock, tôn trọng trạng thái mở bán (`IsPurchasable`), xử lý giá rỗng (`nullable price`), cơ chế làm xám giỏ hàng (`cart gray-out`) và xác thực đổi biến thể sản phẩm.

---

## Clarifications

### Session 2026-11-03
- Q: Khi CatalogService bị gián đoạn kết nối hoặc phản hồi lỗi 5xx trong lúc người mua tải danh sách giỏ hàng (`GET /api/v1/carts/me`), CommerceService nên áp dụng chiến lược chịu lỗi nào? → A: Option A (Fail-Closed: Ném ngoại lệ `CommerceDomainException` trả về HTTP 503 `CATALOG_SERVICE_UNAVAILABLE` ngay lập tức để bảo đảm an toàn dữ liệu và cảnh báo hệ thống, không hiển thị dữ liệu cũ hay giá mock).
- Q: Khi người dùng thực hiện kiểm tra giỏ hàng (`POST /api/v1/carts/me/validate`) và hệ thống phát hiện đơn giá thực tế bên Catalog bị thay đổi so với snapshot trong giỏ hàng, CommerceService nên cập nhật cơ sở dữ liệu như thế nào? → A: Option A (Cập nhật tức thì: Tự động cập nhật `item.UnitPriceSnapshot = catalogSku.UnitPrice` và `UpdatedAt = UtcNow`, commit xuống PostgreSQL ngay trong hàm Validate, đồng thời trả về danh sách issues kèm thông báo giá đã được làm mới).
- Q: Khi một mặt hàng trong giỏ hàng rơi vào tình trạng đồng thời: vừa bị người bán ngừng kinh doanh/ẩn trên Catalog (`IsPurchasable = false`), vừa hết hàng trong kho Fulfillment (`AvailableStock <= 0`), CommerceService nên ưu tiên hiển thị lý do vô hiệu hóa nào? → A: Option A (Ưu tiên Catalog: Kiểm tra Catalog trước; gán `PRODUCT_INACTIVE` thay vì `OUT_OF_STOCK` để người mua hiểu rõ sản phẩm đã ngừng kinh doanh và chủ động xóa khỏi giỏ).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Tự động Làm Xám Món Hàng Không Khả Dụng trong Giỏ Hàng (Priority: P1) 🎯 MVP

Là một người mua hàng mở xem giỏ hàng (`GET /api/v1/carts/me`), tôi muốn các mặt hàng đã bị người bán ẩn, xóa hoặc ngừng kinh doanh trên Catalog (hoặc hết hàng trong kho) phải được hiển thị xám và vô hiệu hóa nút thanh toán, kèm theo lý do cụ thể (`PRODUCT_INACTIVE`, `OUT_OF_STOCK`, `PRODUCT_NOT_FOUND`), để tôi không bị gián đoạn hay gặp lỗi bất ngờ khi tiến hành đặt hàng.

**Why this priority**: Trực tiếp giải quyết trải nghiệm mua sắm cốt lõi của người dùng. Ngăn chặn người mua đặt phải sản phẩm đã ngừng bán hoặc không còn tồn tại trên sàn.

**Independent Test**: Có thể kiểm thử độc lập bằng cách thêm sản phẩm vào giỏ hàng, sau đó chuyển trạng thái sản phẩm bên Catalog sang không mở bán (`IsPurchasable = false`), rồi tải lại giỏ hàng và kiểm tra xem món hàng có được gán cờ không khả dụng (`isAvailable: false`) cùng lý do tương ứng hay không.

**Acceptance Scenarios**:
1. **Given** một mặt hàng trong giỏ đang hoạt động bình thường, **When** người mua xem giỏ hàng, **Then** mặt hàng hiển thị trạng thái sẵn sàng mua sắm (`isAvailable: true`, không có `disabledReason`).
2. **Given** một sản phẩm bị người bán chuyển sang trạng thái ngưng kinh doanh hoặc bị xóa bên Catalog (`isPurchasable: false`), **When** người mua xem giỏ hàng, **Then** dòng sản phẩm đó bị vô hiệu hóa với lý do sản phẩm ngừng bán (`isAvailable: false`, `disabledReason: "PRODUCT_INACTIVE"`).
3. **Given** một SKU trong giỏ hàng không còn tồn tại trong hệ thống Catalog, **When** người mua xem giỏ hàng, **Then** dòng sản phẩm đó bị vô hiệu hóa với lý do không tìm thấy sản phẩm (`isAvailable: false`, `disabledReason: "PRODUCT_NOT_FOUND"`).
4. **Given** một sản phẩm vẫn mở bán nhưng kho hàng báo hết số lượng khả dụng, **When** người mua xem giỏ hàng, **Then** dòng sản phẩm đó bị vô hiệu hóa với lý do hết hàng (`isAvailable: false`, `disabledReason: "OUT_OF_STOCK"`).

---

### User Story 2 - Nguồn Chân Lý Duy Nhất và Chấm Dứt Fallback Giá Ảo (Priority: P1)

Là một quản trị viên nền tảng và bộ phận tài chính, tôi muốn hệ thống Commerce luôn tôn trọng CatalogService là Nguồn Chân lý duy nhất về giá niêm yết và trạng thái mở bán, tuyệt đối không được tự động sinh giá giả lập (như 100.000 VNĐ) hay người bán ảo khi dịch vụ Catalog gặp sự cố tạm thời, mà phải phản hồi lỗi dịch vụ rõ ràng (HTTP 503) để bảo vệ tính toàn vẹn dữ liệu.

**Why this priority**: Loại bỏ rủi ro gian lận giá, chênh lệch thất thoát doanh thu và đảm bảo an toàn vận hành ở cấp độ hệ thống.

**Independent Test**: Gửi request kiểm tra thông tin biến thể khi CatalogService tạm thời mất kết nối; hệ thống phải ném ngoại lệ dịch vụ không khả dụng (`503 Service Unavailable`) mà không được trả về giá trị mặc định 100.000 VNĐ.

**Acceptance Scenarios**:
1. **Given** phản hồi từ Catalog chứa trường trạng thái mở bán `isPurchasable`, **When** Commerce xử lý dữ liệu biến thể, **Then** Commerce sử dụng trực tiếp giá trị này mà không tự tính toán đè lại bằng các phép kiểm tra giá cục bộ.
2. **Given** sản phẩm đấu giá hoặc sản phẩm mẫu chưa có giá niêm yết (giá `null`), **When** hệ thống nhận dữ liệu từ Catalog, **Then** hệ thống tiếp nhận an toàn và gán giá trị 0 VNĐ mà không làm gián đoạn tiến trình phân tích dữ liệu.
3. **Given** CatalogService gặp sự cố ngắt kết nối hoặc trả về lỗi máy chủ nội bộ (5xx), **When** Commerce thực hiện tra cứu biến thể, **Then** Commerce từ chối xử lý và trả về mã lỗi chuẩn dịch vụ tạm thời không khả dụng (`CATALOG_SERVICE_UNAVAILABLE`, HTTP 503).

---

### User Story 3 - Xác Thực Chuyển Đổi Biến Thể Trong Giỏ Hàng (Priority: P2)

Là một người mua hàng muốn đổi phân loại sản phẩm (ví dụ đổi từ Size L sang Size XL, hoặc Màu Đen sang Màu Trắng) ngay trong giỏ hàng, tôi muốn hệ thống xác thực tính hợp lệ của phân loại mới (phải cùng sản phẩm cha và cùng người bán) và cập nhật đơn giá mới, đồng thời từ chối các hành vi đổi phân loại bất hợp lệ sang sản phẩm của người bán khác.

**Why this priority**: Cho phép người mua thuận tiện thay đổi thuộc tính sản phẩm trong giỏ hàng mà vẫn đảm bảo tính toàn vẹn của giỏ hàng và danh tính người bán.

**Independent Test**: Thực hiện yêu cầu đổi biến thể giữa 2 SKU cùng sản phẩm cha (thành công, cập nhật giá mới) và giữa 2 SKU thuộc 2 sản phẩm cha khác nhau (bị từ chối với mã lỗi `CATALOG_VARIANT_MISMATCH`).

**Acceptance Scenarios**:
1. **Given** người mua chọn một phân loại mới cùng sản phẩm cha và cùng người bán, **When** gửi yêu cầu đổi biến thể, **Then** hệ thống chấp thuận, cập nhật mã SKU mới, tên hiển thị mới và đơn giá mới vào dòng giỏ hàng.
2. **Given** người mua chọn một phân loại thuộc sản phẩm cha khác hoặc của người bán khác, **When** gửi yêu cầu đổi biến thể, **Then** hệ thống từ chối với mã lỗi phân loại không khớp (`CATALOG_VARIANT_MISMATCH`, HTTP 400).
3. **Given** người mua chọn một biến thể không còn tồn tại trên hệ thống, **When** gửi yêu cầu đổi biến thể, **Then** hệ thống từ chối với mã lỗi không tìm thấy phân loại (`CATALOG_SKU_NOT_FOUND`, HTTP 404).

---

### User Story 4 - Bảo Vệ Chống Biến Động Giá Bất Đối Xứng Khi Đặt Hàng (Priority: P2)

Là một người mua hàng tiến hành thanh toán đơn hàng, tôi muốn được tự động hưởng lợi nếu sản phẩm được người bán giảm giá, nhưng nếu sản phẩm bị tăng giá, hệ thống phải chặn lại và thông báo xung đột giá (`PRICE_CHANGED`, HTTP 409), đồng thời cập nhật giỏ hàng với mức giá mới nhất để tôi xem xét lại trước khi quyết định mua.

**Why this priority**: Bảo vệ quyền lợi tài chính minh bạch cho người mua hàng, tuân thủ luật bảo vệ người tiêu dùng và các tiêu chuẩn sàn thương mại điện tử.

**Independent Test**: Tạo đơn hàng với một sản phẩm vừa được tăng giá trên Catalog; hệ thống phải từ chối đơn hàng với HTTP 409, cập nhật lại giá trong giỏ hàng và hiển thị thông điệp cảnh báo rõ ràng.

**Acceptance Scenarios**:
1. **Given** một mặt hàng trong giỏ được giảm giá trên Catalog so với lúc thêm vào giỏ, **When** người mua tiến hành đặt hàng, **Then** hệ thống tự động áp dụng mức giá thấp hơn có lợi cho người mua và cho phép hoàn tất đơn hàng.
2. **Given** một mặt hàng trong giỏ bị tăng giá trên Catalog, **When** người mua tiến hành đặt hàng, **Then** hệ thống chặn giao dịch, lưu cập nhật giá mới vào giỏ hàng và trả về thông báo lỗi thay đổi giá (`PRICE_CHANGED`, HTTP 409).
3. **Given** đơn hàng là loại hình đấu giá với giá niêm yết bằng 0, **When** người mua đặt hàng, **Then** hệ thống tôn trọng mức giá thắng đấu giá mà không kích hoạt quy tắc chặn tăng giá của đơn hàng thông thường.

---

### Edge Cases

- **Giá sản phẩm trả về null từ Catalog**: Xảy ra đối với sản phẩm đấu giá chưa kết thúc hoặc bản nháp sản phẩm; hệ thống phải tiếp nhận an toàn và chuyển đổi thành 0 VNĐ mà không gây lỗi phân tích dữ liệu.
- **Số lượng biến thể tra cứu vượt quá 100 mã SKU**: Hệ thống phải tự động chia nhỏ danh sách thành các lô con ($\le 100$ phần tử mỗi lô) để tránh vượt quá tải trọng tối đa của Catalog.
- **Dịch vụ Catalog không phản hồi hoặc timeout khi người dùng tải giỏ hàng**: Hệ thống trả về lỗi 503 `CATALOG_SERVICE_UNAVAILABLE`, tuyệt đối không giả mạo giá 100.000 VNĐ để người dùng đặt hàng sai giá.
- **Sản phẩm bị xóa mềm hoặc ẩn trong khi người dùng đang ở màn hình thanh toán**: Hệ thống phát hiện sản phẩm không còn mở bán (`isPurchasable: false`) và từ chối tạo đơn hàng, yêu cầu người dùng cập nhật lại giỏ hàng.
- **Thực hiện đổi biến thể sang một SKU đã hết hàng hoặc ngừng bán**: Hệ thống trả về thông tin biến thể kèm cờ không khả dụng để giao diện thông báo cho người dùng trước khi xác nhận đổi.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Hệ thống BẮT BUỘC sử dụng trực tiếp trường dữ liệu mở bán `IsPurchasable` từ CatalogService và KHÔNG ĐƯỢC PHÉP tự ghi đè bằng các quy tắc kiểm tra giá cục bộ.
- **FR-002**: Hệ thống BẮT BUỘC hỗ trợ trường giá sản phẩm có thể mang giá trị rỗng (`nullable decimal`) từ Catalog, mặc định chuyển thành 0 VNĐ mà không gây lỗi dừng chương trình.
- **FR-003**: Hệ thống BẮT BUỘC loại bỏ hoàn toàn các cơ chế ngầm định tự sinh giá giả (như 100.000 VNĐ) hoặc người bán giả lập khi không nhận được dữ liệu hợp lệ từ CatalogService.
- **FR-004**: Hệ thống BẮT BUỘC áp dụng nguyên tắc fail-closed, trả về mã lỗi chuẩn RFC 9110 với mã `CATALOG_SERVICE_UNAVAILABLE` (HTTP 503) khi mất kết nối, timeout hoặc CatalogService trả về mã lỗi máy chủ (5xx) trong toàn bộ các tác vụ (bao gồm cả tải giỏ hàng `GET /api/v1/carts/me`, kiểm tra `POST /api/v1/carts/me/validate` và tạo đơn `POST /api/v1/orders`), triệt tiêu hoàn toàn việc dùng dữ liệu cũ hoặc mock giả lập.
- **FR-005**: Hệ thống BẮT BUỘC thực hiện tra cứu thông tin sản phẩm từ CatalogService song song với kiểm tra tồn kho từ FulfillmentService khi người dùng xem chi tiết giỏ hàng (`GET /api/v1/carts/me`).
- **FR-006**: Hệ thống BẮT BUỘC đánh dấu cờ không khả dụng (`isAvailable: false`) cho dòng giỏ hàng tương ứng theo thứ tự ưu tiên: (1) `"PRODUCT_NOT_FOUND"` nếu không tìm thấy SKU trong Catalog, (2) `"PRODUCT_INACTIVE"` nếu Catalog báo ngưng mở bán (`IsPurchasable = false` - ưu tiên cao hơn hết hàng), (3) `"OUT_OF_STOCK"` nếu sản phẩm mở bán bình thường nhưng kho Fulfillment báo hết hàng khả dụng.
- **FR-007**: Hệ thống BẮT BUỘC xác thực yêu cầu đổi phân loại sản phẩm trong giỏ hàng qua CatalogService, đảm bảo biến thể mới cùng sản phẩm cha và cùng người bán; từ chối với lỗi `CATALOG_VARIANT_MISMATCH` (HTTP 400) nếu vi phạm.
- **FR-008**: Hệ thống BẮT BUỘC tự động phân tách danh sách SKU thành các lô nhỏ tối đa 100 mã khi gọi API tra cứu hàng loạt sang CatalogService.
- **FR-009**: Hệ thống BẮT BUỘC cập nhật tức thì đơn giá ghi nhận (`UnitPriceSnapshot`) và thời gian cập nhật (`UpdatedAt`) của các món trong giỏ hàng xuống cơ sở dữ liệu khi phát hiện chênh lệch giá trong quá trình xác thực giỏ (`POST /api/v1/carts/me/validate`), đồng thời trả về danh sách cảnh báo `issues` cho người dùng.
- **FR-010**: Hệ thống BẮT BUỘC áp dụng chiến lược biến động giá bất đối xứng khi tạo đơn hàng: tự động cập nhật giảm giá, nhưng chặn đơn hàng và trả về lỗi `PRICE_CHANGED` (HTTP 409) nếu có sản phẩm tăng giá, sau khi đã đồng bộ giá mới vào cơ sở dữ liệu giỏ hàng.

---

### Key Entities *(include if feature involves data)*

- **Biến Thể Sản Phẩm (Variant Info)**: Đại diện cho dữ liệu mặt hàng được cung cấp bởi CatalogService; bao gồm mã biến thể (`VariantId`), mã sản phẩm cha (`ProductId`), mã người bán (`SellerId`), tên hiển thị (`ProductName`), đơn giá (`UnitPrice`), loại hình bán (`SaleType`), và trạng thái mở bán (`IsPurchasable`).
- **Dòng Giỏ Hàng (Cart Item)**: Đại diện cho mặt hàng mà người mua chọn lưu trong giỏ; bao gồm thông tin biến thể liên kết, số lượng, đơn giá ghi nhận tại thời điểm chọn (`UnitPriceSnapshot`), trạng thái khả dụng hiện thời (`IsAvailable`), và lý do vô hiệu hóa nếu có (`DisabledReason`).
- **Kết Quả Xác Thực Đổi Biến Thể (Variant Switch Result)**: Kết quả thẩm định tính hợp lệ khi người mua đổi tùy chọn sản phẩm trong giỏ; chứa trạng thái hợp lệ, mã sản phẩm cha, mã người bán, đơn giá mới và trạng thái mở bán của tùy chọn mới.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% các sản phẩm bị ngừng bán hoặc không còn tồn tại trên Catalog được làm xám và chặn thanh toán chính xác ngay khi người mua tải lại giỏ hàng.
- **SC-002**: 0% trường hợp phát sinh giá ảo (như 100.000 VNĐ) hoặc thông tin người bán giả lập xuất hiện trong cơ sở dữ liệu và giao diện người dùng.
- **SC-003**: Thao tác tải giỏ hàng thực hiện kiểm tra Catalog và tồn kho song song, hoàn thành toàn bộ quá trình xác thực trong thời gian phản hồi tiêu chuẩn mà không bị trễ tuần tự.
- **SC-004**: 100% trường hợp tăng giá sản phẩm khi tạo đơn hàng được phát hiện và cảnh báo trước cho người mua, ngăn chặn việc người mua bị thu tiền cao hơn dự kiến mà không có sự đồng ý.
- **SC-005**: 100% bộ kiểm thử tự động (Unit Tests) liên quan đến tích hợp Catalog và Giỏ hàng chạy thành công với 0 cảnh báo và 0 lỗi.

---

## Assumptions

- **Ranh giới dịch vụ**: CatalogService là nguồn sự thật duy nhất về danh mục, thông tin sản phẩm và giá niêm yết. Việc quản lý và giữ số lượng tồn kho thuộc về FulfillmentService (không liên quan đến CatalogService).
- **Cổng kết nối**: CatalogService chạy tại cổng `8002` trong môi trường nội bộ và giao tiếp hoàn toàn qua giao thức RESTful HTTP/1.1 (hoặc HTTP/2) với Header xác thực `X-Internal-Service-Key`.
- **Đơn hàng Đấu giá**: Sản phẩm đấu giá có cơ chế định giá đặc thù dựa trên số tiền trúng đấu giá (`WinningAmount`) và được miễn trừ khỏi quy tắc so sánh tăng giá của sản phẩm thông thường.
- **Khả năng chịu lỗi**: Khi CatalogService không phản hồi, giải pháp an toàn nhất là tạm ngừng phục vụ giao dịch cho các mặt hàng bị ảnh hưởng (fail-closed) thay vì ước đoán giá.
