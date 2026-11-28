# COMMERCE SERVICE — TOÀN BỘ 72 ĐẶC TẢ API CHUẨN SERVICE-API-DOC

> **Căn cứ quy chuẩn:** Kỹ năng chuyên biệt [service-api-doc](file:///d:/CommerceService/.agents/skills/service-api-doc/SKILL.md) và mẫu tài liệu [SERVICE_API_DOC_TEMPLATE.md](file:///d:/CommerceService/docs/api/SERVICE_API_DOC_TEMPLATE.md).
> **Nguyên tắc cốt lõi:** 100% Inward-Only, Pure DTO (không bọc `ApiResponse<T>`), 100% English sentence-case error messages, PascalCase enum serialization.

---

## Danh mục 72 Endpoint Chi Tiết Theo Phân Hệ (Feature Breakdown)

### Orders (28 Endpoints)

> **Lưu ý:** Endpoint hủy đơn cha `POST /api/v1/orders/{orderId}/cancel` đã bị gỡ bỏ (`REMOVED`). Người mua chỉ hủy từng đơn hàng con trước khi người bán xác nhận qua `POST /api/v1/sub-orders/{subOrderId}/cancel`.

| HTTP | Route Endpoint | Tên / Nghiệp Vụ Cốt Lõi | File Đặc Tả Markdown |
| :-: | - | - | - |
| `POST` | `/api/v1/checkouts/quote` | Checkout Preview & Shipping Fee Quotation (15-min TTL) | [POST_CHECKOUTS_QUOTE.md](file:///d:/CommerceService/docs/api/Orders/POST_CHECKOUTS_QUOTE.md) |
| `POST` | `/api/v1/checkouts/quote/{quoteId}/recalculate` | Recalculate Checkout Quote Upon Address Change | [POST_CHECKOUTS_QUOTE_RECALCULATE.md](file:///d:/CommerceService/docs/api/Orders/POST_CHECKOUTS_QUOTE_RECALCULATE.md) |
| `GET` | `/api/v1/internal/orders/dispute-eligibility` | Verify Dispute Eligibility for UserService | [GET_INTERNAL_ORDERS_DISPUTE_ELIGIBILITY.md](file:///d:/CommerceService/docs/api/Orders/GET_INTERNAL_ORDERS_DISPUTE_ELIGIBILITY.md) |
| `POST` | `/api/v1/internal/sub-orders/{subOrderId}/dispute-refund` | Dispute Resolution Refund S2S | [POST_INTERNAL_SUB_ORDERS_DISPUTE_REFUND.md](file:///d:/CommerceService/docs/api/Orders/POST_INTERNAL_SUB_ORDERS_DISPUTE_REFUND.md) |
| `POST` | `/api/v1/orders` | Create Master Order & SubOrders | [POST_ORDERS.md](file:///d:/CommerceService/docs/api/Orders/POST_ORDERS.md) |
| `GET` | `/api/v1/orders/{orderId}` | Get Master Order Details | [GET_ORDERS_DETAIL.md](file:///d:/CommerceService/docs/api/Orders/GET_ORDERS_DETAIL.md) |
| `GET` | `/api/v1/orders` | List My Master Orders History | [GET_ORDERS_LIST.md](file:///d:/CommerceService/docs/api/Orders/GET_ORDERS_LIST.md) |
| `GET` | `/api/v1/sub-orders` | List Seller Sub-Orders | [GET_SUB_ORDERS_LIST.md](file:///d:/CommerceService/docs/api/Orders/GET_SUB_ORDERS_LIST.md) |
| `GET` | `/api/v1/sub-orders/{subOrderId}` | Get Sub-Order Detail by ID | [GET_SUB_ORDERS_DETAIL.md](file:///d:/CommerceService/docs/api/Orders/GET_SUB_ORDERS_DETAIL.md) |
| `POST` | `/api/v1/orders/{orderId}/accept` | Seller Accept Sub-Order & Generate Shipment | [POST_ORDERS_ACCEPT.md](file:///d:/CommerceService/docs/api/Orders/POST_ORDERS_ACCEPT.md) |
| `POST` | `/api/v1/orders/{orderId}/reject` | Seller Reject Sub-Order & Refund | [POST_ORDERS_REJECT.md](file:///d:/CommerceService/docs/api/Orders/POST_ORDERS_REJECT.md) |
| `GET` | `/api/v1/orders/{orderId}/shipments` | Get Master Order Shipments Tracking | [GET_SHIPMENTS_ORDER_TRACKING.md](file:///d:/CommerceService/docs/api/Orders/GET_SHIPMENTS_ORDER_TRACKING.md) |
| `POST` | `/api/v1/orders/{orderId}/complete` | Complete Entire Master Order | [POST_ORDERS_COMPLETE.md](file:///d:/CommerceService/docs/api/Orders/POST_ORDERS_COMPLETE.md) |
| `POST` | `/api/v1/sub-orders/{subOrderId}/confirm-receipt` | Buyer Confirm Sub-Order Receipt & Release Escrow | [POST_SUB_ORDERS_CONFIRM_RECEIPT.md](file:///d:/CommerceService/docs/api/Orders/POST_SUB_ORDERS_CONFIRM_RECEIPT.md) |
| `POST` | `/api/v1/sub-orders/{subOrderId}/cancel` | Buyer Cancel Single Sub-Order | [POST_SUB_ORDERS_CANCEL.md](file:///d:/CommerceService/docs/api/Orders/POST_SUB_ORDERS_CANCEL.md) |
| `POST` | `/api/v1/sub-orders/{subOrderId}/refund` | Request Sub-Order Refund | [POST_SUB_ORDERS_REFUND.md](file:///d:/CommerceService/docs/api/Orders/POST_SUB_ORDERS_REFUND.md) |
| `GET` | `/api/v1/internal/review-eligibility` | Verify Review Eligibility for UserService | [GET_INTERNAL_REVIEW_ELIGIBILITY.md](file:///d:/CommerceService/docs/api/Orders/GET_INTERNAL_REVIEW_ELIGIBILITY.md) |
| `POST` | `/api/v1/sub-orders/{subOrderId}/delivery-status` | Carrier Delivery Status Webhook Ingestion | [POST_SUB_ORDERS_DELIVERY_STATUS.md](file:///d:/CommerceService/docs/api/Orders/POST_SUB_ORDERS_DELIVERY_STATUS.md) |
| `POST` | `/api/v1/sub-orders/{subOrderId}/shipments/retry` | Retry Failed Sub-Order Shipment Creation | [POST_SUB_ORDERS_SHIPMENTS_RETRY.md](file:///d:/CommerceService/docs/api/Orders/POST_SUB_ORDERS_SHIPMENTS_RETRY.md) |
| `GET` | `/api/v1/sub-orders/{subOrderId}/shipments/timeline` | Get Sub-Order Shipment Timeline | [GET_SUB_ORDERS_SHIPMENT_TIMELINE.md](file:///d:/CommerceService/docs/api/Orders/GET_SUB_ORDERS_SHIPMENT_TIMELINE.md) |
| `POST` | `/api/v1/orders/{orderId}/pay` | Repay Overdue/Pending Order via Wallet | [POST_ORDERS_PAY.md](file:///d:/CommerceService/docs/api/Orders/POST_ORDERS_PAY.md) |
| `GET` | `/api/v1/orders/{orderId}/refunds` | Get Order Refund History | [GET_ORDERS_REFUNDS.md](file:///d:/CommerceService/docs/api/Orders/GET_ORDERS_REFUNDS.md) |
| `GET` | `/api/v1/sub-orders/{subOrderId}/financial-breakdown` | Sub-Order Financial Breakdown for Seller | [GET_SUB_ORDERS_FINANCIAL_BREAKDOWN.md](file:///d:/CommerceService/docs/api/Orders/GET_SUB_ORDERS_FINANCIAL_BREAKDOWN.md) |
| `GET` | `/api/v1/orders/buyer/{buyerId}` | List Orders by Buyer ID (Legacy/Admin) | [GET_ORDERS_BUYER.md](file:///d:/CommerceService/docs/api/Orders/GET_ORDERS_BUYER.md) |
| `PATCH` | `/api/v1/sub-orders/{subOrderId}/shipping-address` | Change Sub-Order Shipping Address | [PATCH_SUB_ORDERS_SHIPPING_ADDRESS.md](file:///d:/CommerceService/docs/api/Orders/PATCH_SUB_ORDERS_SHIPPING_ADDRESS.md) |
| `PATCH` | `/api/v1/orders/{orderId}/shipping-address` | Change Master Order Shipping Address | [PATCH_ORDERS_SHIPPING_ADDRESS.md](file:///d:/CommerceService/docs/api/Orders/PATCH_ORDERS_SHIPPING_ADDRESS.md) |
| `GET` | `/api/v1/admin/orders` | Admin Centralized Master Orders Query | [GET_ADMIN_ORDERS.md](file:///d:/CommerceService/docs/api/Orders/GET_ADMIN_ORDERS.md) |
| `GET` | `/api/v1/admin/sub-orders` | Admin Centralized Sub-Orders Query | [GET_ADMIN_SUB_ORDERS.md](file:///d:/CommerceService/docs/api/Orders/GET_ADMIN_SUB_ORDERS.md) |

### Carts (14 Endpoints)

| HTTP | Route Endpoint | Tên / Nghiệp Vụ Cốt Lõi | File Đặc Tả Markdown |
| :-: | - | - | - |
| `GET` | `/api/v1/carts/me` | Retrieve Active Shopping Cart | [GET_CARTS_ME.md](file:///d:/CommerceService/docs/api/Carts/GET_CARTS_ME.md) |
| `GET` | `/api/v1/carts/me/summary` | Cart Badge Counter & Estimated Total | [GET_CARTS_ME_SUMMARY.md](file:///d:/CommerceService/docs/api/Carts/GET_CARTS_ME_SUMMARY.md) |
| `GET` | `/api/v1/carts/me/grouped` | Group Cart Items by Shop (Multi-Vendor View) | [GET_CARTS_ME_GROUPED.md](file:///d:/CommerceService/docs/api/Carts/GET_CARTS_ME_GROUPED.md) |
| `POST` | `/api/v1/carts/me/items` | Add Item to Shopping Cart | [POST_CARTS_ME_ITEMS.md](file:///d:/CommerceService/docs/api/Carts/POST_CARTS_ME_ITEMS.md) |
| `PATCH` | `/api/v1/carts/me/items/{cartItemId}` | Update Cart Item Quantity | [PATCH_CARTS_ME_ITEMS_QUANTITY.md](file:///d:/CommerceService/docs/api/Carts/PATCH_CARTS_ME_ITEMS_QUANTITY.md) |
| `PATCH` | `/api/v1/carts/me/items/{cartItemId}/variant` | Change Product Variant in Cart | [PATCH_CARTS_ME_ITEMS_VARIANT.md](file:///d:/CommerceService/docs/api/Carts/PATCH_CARTS_ME_ITEMS_VARIANT.md) |
| `DELETE` | `/api/v1/carts/me/items/{cartItemId}` | Remove Single Item from Cart | [DELETE_CARTS_ME_ITEMS.md](file:///d:/CommerceService/docs/api/Carts/DELETE_CARTS_ME_ITEMS.md) |
| `POST` | `/api/v1/carts/me/items:bulk-remove` | Bulk Remove Items from Cart | [POST_CARTS_ME_ITEMS_BULK_REMOVE.md](file:///d:/CommerceService/docs/api/Carts/POST_CARTS_ME_ITEMS_BULK_REMOVE.md) |
| `DELETE` | `/api/v1/carts/me/clear` | Clear Entire Shopping Cart | [DELETE_CARTS_ME_CLEAR.md](file:///d:/CommerceService/docs/api/Carts/DELETE_CARTS_ME_CLEAR.md) |
| `POST` | `/api/v1/carts/me/calculate` | Calculate Subtotal for Selected Items (Checkbox) | [POST_CARTS_ME_CALCULATE.md](file:///d:/CommerceService/docs/api/Carts/POST_CARTS_ME_CALCULATE.md) |
| `POST` | `/api/v1/carts/me/validate` | Validate Cart Items Fresh Price & Stock | [POST_CARTS_ME_VALIDATE.md](file:///d:/CommerceService/docs/api/Carts/POST_CARTS_ME_VALIDATE.md) |
| `POST` | `/api/v1/carts/me/merge` | Merge Guest Cart Upon User Login | [POST_CARTS_ME_MERGE.md](file:///d:/CommerceService/docs/api/Carts/POST_CARTS_ME_MERGE.md) |
| `POST` | `/api/v1/carts/me/cleanup-checked-out` | Cleanup Checked Out Items | [POST_CARTS_ME_CLEANUP_CHECKED_OUT.md](file:///d:/CommerceService/docs/api/Carts/POST_CARTS_ME_CLEANUP_CHECKED_OUT.md) |
| `GET` | `/api/v1/carts/active` | Retrieve Active Cart by Buyer ID (Support/Admin) | [GET_CARTS_ACTIVE.md](file:///d:/CommerceService/docs/api/Carts/GET_CARTS_ACTIVE.md) |

### AuctionOrders (14 Endpoints)

| HTTP | Route Endpoint | Tên / Nghiệp Vụ Cốt Lõi | File Đặc Tả Markdown |
| :-: | - | - | - |
| `POST` | `/api/v1/auction-orders/settlement` | Auction Settlement Ingestion | [POST_AUCTION_ORDERS_SETTLEMENT.md](file:///d:/CommerceService/docs/api/AuctionOrders/POST_AUCTION_ORDERS_SETTLEMENT.md) |
| `PUT` | `/api/v1/auction-orders/{auctionOrderId}/shipping` | Submit Auction Shipping Address & Quote Fee | [PUT_AUCTION_ORDERS_SHIPPING.md](file:///d:/CommerceService/docs/api/AuctionOrders/PUT_AUCTION_ORDERS_SHIPPING.md) |
| `POST` | `/api/v1/auction-orders/{auctionOrderId}/pay` | Pay Auction Order via Wallet | [POST_AUCTION_ORDERS_PAY.md](file:///d:/CommerceService/docs/api/AuctionOrders/POST_AUCTION_ORDERS_PAY.md) |
| `POST` | `/api/v1/auction-orders/{auctionOrderId}/accept` | Seller Accept Auction Order | [POST_AUCTION_ORDERS_ACCEPT.md](file:///d:/CommerceService/docs/api/AuctionOrders/POST_AUCTION_ORDERS_ACCEPT.md) |
| `POST` | `/api/v1/auction-orders/{auctionOrderId}/reject` | Seller Reject Auction Order & Refund | [POST_AUCTION_ORDERS_REJECT.md](file:///d:/CommerceService/docs/api/AuctionOrders/POST_AUCTION_ORDERS_REJECT.md) |
| `POST` | `/api/v1/auction-orders/{auctionOrderId}/confirm-receipt` | Confirm Receipt & Settle Escrow | [POST_AUCTION_ORDERS_CONFIRM_RECEIPT.md](file:///d:/CommerceService/docs/api/AuctionOrders/POST_AUCTION_ORDERS_CONFIRM_RECEIPT.md) |
| `GET` | `/api/v1/auction-orders/me` | List My Won Auction Orders | [GET_AUCTION_ORDERS_ME.md](file:///d:/CommerceService/docs/api/AuctionOrders/GET_AUCTION_ORDERS_ME.md) |
| `GET` | `/api/v1/auction-orders/{auctionOrderId}` | Get Auction Order Detail | [GET_AUCTION_ORDERS_DETAIL.md](file:///d:/CommerceService/docs/api/AuctionOrders/GET_AUCTION_ORDERS_DETAIL.md) |
| `GET` | `/api/v1/internal/auction-orders/{orderId}/notification-snapshot` | Internal Notification Snapshot | [GET_INTERNAL_AUCTION_ORDERS_NOTIFICATION_SNAPSHOT.md](file:///d:/CommerceService/docs/api/AuctionOrders/GET_INTERNAL_AUCTION_ORDERS_NOTIFICATION_SNAPSHOT.md) |
| `GET` | `/api/v1/auction-orders/seller` | List Seller Auction Orders | [GET_AUCTION_ORDERS_SELLER.md](file:///d:/CommerceService/docs/api/AuctionOrders/GET_AUCTION_ORDERS_SELLER.md) |
| `PATCH` | `/api/v1/auction-orders/{auctionOrderId}/shipping-address` | Change Auction Shipping Address Before Dispatch | [PATCH_AUCTION_ORDERS_SHIPPING_ADDRESS.md](file:///d:/CommerceService/docs/api/AuctionOrders/PATCH_AUCTION_ORDERS_SHIPPING_ADDRESS.md) |
| `GET` | `/api/v1/auction-orders/{id}/shipments/timeline` | Get Auction Order Shipment Timeline | [GET_AUCTION_ORDERS_SHIPMENTS_TIMELINE.md](file:///d:/CommerceService/docs/api/AuctionOrders/GET_AUCTION_ORDERS_SHIPMENTS_TIMELINE.md) |
| `POST` | `/api/v1/auction-orders/{id}/cancel` | Cancel Auction Order Before Acceptance | [POST_AUCTION_ORDERS_CANCEL.md](file:///d:/CommerceService/docs/api/AuctionOrders/POST_AUCTION_ORDERS_CANCEL.md) |
| `POST` | `/api/v1/auction-orders/{id}/shipments/retry` | Retry Failed Shipment Creation with GHN | [POST_AUCTION_ORDERS_SHIPMENTS_RETRY.md](file:///d:/CommerceService/docs/api/AuctionOrders/POST_AUCTION_ORDERS_SHIPMENTS_RETRY.md) |

### Discounts (9 Endpoints)

| HTTP | Route Endpoint | Tên / Nghiệp Vụ Cốt Lõi | File Đặc Tả Markdown |
| :-: | - | - | - |
| `GET` | `/api/v1/discounts/available` | Query Available Vouchers for Checkout | [GET_DISCOUNTS_AVAILABLE.md](file:///d:/CommerceService/docs/api/Discounts/GET_DISCOUNTS_AVAILABLE.md) |
| `POST` | `/api/v1/discounts/validate` | Validate Single Voucher Against Cart | [POST_DISCOUNTS_VALIDATE.md](file:///d:/CommerceService/docs/api/Discounts/POST_DISCOUNTS_VALIDATE.md) |
| `POST` | `/api/v1/discounts/recommend` | Recommend Optimal Voucher Combination (Shopee Best Deal) | [POST_DISCOUNTS_RECOMMEND.md](file:///d:/CommerceService/docs/api/Discounts/POST_DISCOUNTS_RECOMMEND.md) |
| `POST` | `/api/v1/discounts` | Create New Discount / Voucher | [POST_DISCOUNTS.md](file:///d:/CommerceService/docs/api/Discounts/POST_DISCOUNTS.md) |
| `GET` | `/api/v1/discounts` | List Discounts with Pagination and Filter | [GET_DISCOUNTS.md](file:///d:/CommerceService/docs/api/Discounts/GET_DISCOUNTS.md) |
| `GET` | `/api/v1/discounts/{discountId}` | Get Discount Details by ID | [GET_DISCOUNTS_DETAIL.md](file:///d:/CommerceService/docs/api/Discounts/GET_DISCOUNTS_DETAIL.md) |
| `PUT` | `/api/v1/discounts/{discountId}` | Update Discount Information | [PUT_DISCOUNTS.md](file:///d:/CommerceService/docs/api/Discounts/PUT_DISCOUNTS.md) |
| `PATCH` | `/api/v1/discounts/{discountId}/status` | Update Discount Status | [PATCH_DISCOUNTS_STATUS.md](file:///d:/CommerceService/docs/api/Discounts/PATCH_DISCOUNTS_STATUS.md) |
| `DELETE` | `/api/v1/discounts/{discountId}` | Soft Delete Discount | [DELETE_DISCOUNTS.md](file:///d:/CommerceService/docs/api/Discounts/DELETE_DISCOUNTS.md) |

### Refunds (4 Endpoints)

| HTTP | Route Endpoint | Tên / Nghiệp Vụ Cốt Lõi | File Đặc Tả Markdown |
| :-: | - | - | - |
| `GET` | `/api/v1/refunds` | List Order Refund Requests | [GET_REFUNDS.md](file:///d:/CommerceService/docs/api/Refunds/GET_REFUNDS.md) |
| `GET` | `/api/v1/refunds/{id}` | Get Refund Request Detail by ID | [GET_REFUNDS_DETAIL.md](file:///d:/CommerceService/docs/api/Refunds/GET_REFUNDS_DETAIL.md) |
| `POST` | `/api/v1/refunds/{id}/approve` | Approve Order Refund & Credit Wallet | [POST_REFUNDS_APPROVE.md](file:///d:/CommerceService/docs/api/Refunds/POST_REFUNDS_APPROVE.md) |
| `POST` | `/api/v1/refunds/{id}/reject` | Reject Order Refund Request | [POST_REFUNDS_REJECT.md](file:///d:/CommerceService/docs/api/Refunds/POST_REFUNDS_REJECT.md) |

### Admin (2 Endpoints)

| HTTP | Route Endpoint | Tên / Nghiệp Vụ Cốt Lõi | File Đặc Tả Markdown |
| :-: | - | - | - |
| `GET` | `/api/v1/admin/platform-fee` | Retrieve Platform Commission Fee Configuration | [GET_ADMIN_PLATFORM_FEE.md](file:///d:/CommerceService/docs/api/Admin/GET_ADMIN_PLATFORM_FEE.md) |
| `PUT` | `/api/v1/admin/platform-fee` | Update Platform Commission Fee Configuration | [PUT_ADMIN_PLATFORM_FEE.md](file:///d:/CommerceService/docs/api/Admin/PUT_ADMIN_PLATFORM_FEE.md) |

---

> **Master Contract:** Hợp đồng tổng hợp toàn diện hệ thống tiếp tục được lưu giữ tại [docs/COMMERCE_API_CONTRACT.md](file:///d:/CommerceService/docs/COMMERCE_API_CONTRACT.md).
