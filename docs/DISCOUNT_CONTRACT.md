# Discount / voucher API contract (P2-01)

`POST /api/v1/checkouts/quote` now reports what really happened to each requested voucher.

- `appliedVouchers`: only codes the engine applied (previously it echoed the request when nothing applied).
- `appliedVoucherDetails[]`: `{ code, scopeType, ownerSellerId, discountAmount }`.
- `rejectedVouchers[]`: `{ code, reasonCode }`. Reason codes: `NOT_FOUND`, `NOT_ACTIVE`, `NOT_STARTED`, `EXPIRED`, `USAGE_LIMIT_REACHED`, `USER_LIMIT_REACHED`, `MIN_ORDER_NOT_MET`, `SCOPE_MISMATCH`, `EVALUATION_FAILED`, `REJECTED` (fallback).
- Both new fields are optional additions; existing clients keep working.

`POST /api/v1/orders`: if a voucher that the quote showed as applied no longer applies, the order is refused with `409 VOUCHER_NO_LONGER_VALID` (before, it was created at full price). Reservation takes a per-voucher advisory lock inside a transaction, so the total limit cannot be exceeded by concurrent orders.

Stacking rules are unchanged: at most one System voucher per order, at most one Shop/Product voucher per shop (`SHOP_MULTIPLE_VOUCHERS_NOT_ALLOWED`, `VOUCHER_STACKING_CONFLICT`, HTTP 400).

Admin operations (create, update, status change, delete) write `AuditLogs` rows (`DiscountCreate`, `DiscountUpdate`, `DiscountStatusChange`, `DiscountDelete`).
