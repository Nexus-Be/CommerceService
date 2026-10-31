# 05 — ERROR HANDLING & ENUM BOUNDARY (COMMERCE SERVICE)

## 1. 100% English & Sentence Case
* Error messages must be 100% English sentence case.
* Capitalize only the first letter. Keep technical acronyms uppercase (`ID`, `SKU`, `UUID`).
* Examples: `"Cart item not found."`, `"Order cannot be cancelled in its current status."`, `"Active cart not found for the specified buyer."`

## 2. SCREAMING_SNAKE_CASE Error Codes
* Error codes must be uppercase with underscores. No `ERR_` prefixes.
* Use `VAL_` prefix for validation errors.
* Examples: `CART_NOT_FOUND`, `ORDER_NOT_FOUND`, `INVALID_ORDER_STATUS`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`.

## 3. RFC 9110 ProblemDetails Structure
* Handled by `GlobalExceptionHandlerMiddleware.cs`: returns `application/problem+json` containing `type`, `title`, `status`, `errorCode`, `detail`, `traceId`.

## 4. Enum Persistence & Boundary Serialization
* In PostgreSQL (`commerce_db`): 100% stored as `integer` (int32). Prohibit `HasConversion<string>()`.
* Enums: `OrderStatus` (`PendingPayment = 0`, `Paid = 1`, `Processing = 2`, `Delivered = 3`, `Cancelled = 4`), `PaymentStatus`.
* REST Outbound: 100% serialized as `PascalCase` strings (`"PendingPayment"`, `"Paid"`, `"Cancelled"`).
* REST Inbound: Handled via `PascalCaseJsonStringEnumConverter(allowIntegerValues: false)`.
