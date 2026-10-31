# 02 — API ROUTING, ENVELOPES & PAGINATION (COMMERCE SERVICE)

## 1. Route Conventions in CommerceService
* Collections: Lowercase plural nouns (`/api/v1/carts`, `/api/v1/orders`, `/api/v1/sub-orders`, `/api/v1/checkouts`).
* Prohibit singular nouns (`/api/v1/cart`, `/api/v1/order`).
* One route attribute per action. Use single-line XML `<summary>`.

## 2. Pure DTO Return & Zero ResultFilter
* Controllers return pure DTOs:
  * `Ok(cartDto)`
  * `StatusCode(StatusCodes.Status201Created, orderDto)`
  * `NoContent()` (for deletion/empty responses)
  * `Ok(pagedOrders)` (for paginated order lists)
* Zero ResultFilter: Do NOT apply `ResultFilter` or manual envelope factories in `Commerce.Api`. Gateway automatically wraps raw DTOs in `ApiResponse<T>`.

## 3. Database Pagination Governance
* Database-level pagination: `.Skip((page - 1) * pageSize).Take(pageSize)` and `.CountAsync()`.
* Output: Standard `PagedData<T>` envelope containing `items` and `pagination` metadata.
