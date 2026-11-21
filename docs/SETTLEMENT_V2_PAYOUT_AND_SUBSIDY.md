# Seller payout protocol V2, platform voucher subsidy and the V1 cutover

This document describes how Commerce pays sellers (regular and auction orders), how the platform-funded part of a voucher is reserved,
and how the legacy payout route (V1) is retired. The Wallet side is described in `WalletService/docs/api/checkout/order-settlement-v2.md`.

## Money contract

Regular order (wallet payment). `N = items - product discount - shop discount`, `F = round(N * rate)` (whole VND, halves away from zero),
`S` = platform voucher discount, `H` = shipping.

| Quantity | Formula |
|---|---|
| Buyer pays into the order escrow | `T = (N - S) + H` |
| Seller receives | `R = N - F` |
| Identity checked before every payout | `T + S = R + F + H` |

At payout Wallet moves one allocation: the seller gets `R` (an `EscrowRelease` of `R - S` plus a `PlatformSubsidyCredit` of `S` paid from the
reserved budget), the fee `F` and shipping `H` are recorded in the settlement record (they change no wallet balance), and the order escrow
goes down by `T`.

Auction order. `W` = winning amount, `D` = deposit (its own escrow), `F = round(0.05 * W)`, `R = W - F`, `H` = shipping.
The buyer pays `T = (W - D) + H` into the order escrow. Identity: `T + D = R + F + H`. Both escrows are used once. Buy Now (`D = W`, buyer pays
only shipping) and Second Chance follow the same identity.

COD: the platform fee is fixed at 0%, nothing goes through the wallet, and platform vouchers are refused (409 `SYSTEM_VOUCHER_NOT_ALLOWED_FOR_COD`)
until a cash reconciliation path exists.

## Fee rate

The rate is stored in `SystemSettings` (`PLATFORM_FEE_PERCENT`); the cache only speeds reads. The quote carries the rate, order creation reuses it,
and `SubOrders.PlatformFeePercent` / `AuctionOrders.PlatformFeePercent` keep it. A later change never alters an existing order. The admin response
(`GET/PUT /api/v1/admin/platform-fee`) states `appliesTo: "WalletOrders"` and `codPercent: 0`.

## Payout flow (protocol V2)

1. When a sub-order (or auction order) becomes payable, `SettlementService` captures the payload once in `SettlementSnapshots`
   (unique per business type and sub-order) and **commits it before calling Wallet**.
2. It sends that stored payload to `POST /api/v1/internal/wallets/order-settlements`. Retries always resend the stored payload, even if prices or
   the fee rate changed later.
3. Wallet answers: success or replay (same payload) completes the snapshot; a permanent refusal (4xx: conflict, missing source money, bad payload)
   sets `NeedsFinance` and is never retried; a transient failure (timeout, 5xx, feature off) backs off (1, 2, 4 ... minutes, at most 60) and
   moves to `NeedsFinance` after 20 attempts.
4. Only after Wallet confirmed: `SubOrders.IsEscrowSettled = true` (and `SubsidyState = Used`), or `AuctionOrders.PayoutCompletedAt` is set.
5. `SettlementRetryWorker` (every minute) retries due snapshots and releases the subsidy of cancelled or refunded sub-orders. A stuck payout never
   occupies one of the 50 slots of a scan (payout scans exclude `NeedsFinance` and not-yet-due snapshots).

## Platform voucher budget

Wallet owns the budget (second admin approves; per-order cap, daily and total limits). For a wallet order on protocol V2 with a platform discount,
`OrderService` reserves the subsidy per sub-order after stock is held and before the buyer pays:

* No approved budget, over a cap or exhausted: 409 `VOUCHER_BUDGET_EXHAUSTED`; nothing is charged, stock and voucher reservations are released,
  no order is left behind, and the buyer must re-quote to confirm the new total.
* Wallet unreachable: 503 `SUBSIDY_UNAVAILABLE` (fail closed).
* Payment fails definitively or the order cannot be saved: the held subsidies are released.
* Cancelled or refunded before payout: the worker releases it (`SubsidyState` Held -> Released). A used subsidy cannot be released.

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `Settlement:ProtocolVersion` | `1` | Protocol given to **new** orders (`SubOrders.SettlementProtocolVersion`, `AuctionOrders.SettlementProtocolVersion`). Set `2` at the cutover |
| `Settlement:LegacyV1Enabled` | `true` | While `true`, orders on V1 are still paid by the legacy route. Set `false` at the cutover: unpaid V1 orders then continue only through V2 |

Wallet: `ReleaseFeatures:OrderSettlementV2Enabled` (V2 payout and subsidy hold) and `ReleaseFeatures:LegacyCommerceSettlementEnabled` (closes only
the V1 payout routes; falls back to `LegacyCommerceCreditsEnabled` when unset, which keeps controlling the refund routes).

## Cutover runbook (V1 to V2)

1. Deploy Wallet and Commerce with the defaults above (nothing changes). Confirm migrations applied.
2. Create and approve the subsidy budget (two admins) if platform vouchers will be used.
3. Set `Wallet ReleaseFeatures:OrderSettlementV2Enabled=true`, then `Commerce Settlement:ProtocolVersion=2` and `Settlement:LegacyV1Enabled=false`;
   then set `Wallet ReleaseFeatures:LegacyCommerceSettlementEnabled=false` so no new V1 payout can be made.
4. Review `GET /api/v1/internal/reconciliation/legacy-settlement-classes` (internal key) and Wallet reconciliation I3/I4 for the legacy data:
   `V1_UNPAID_CONVERTIBLE` continues through V2; `V1_UNPAID_PLATFORM_DISCOUNT` and every `PAYOUT_NEEDS_FINANCE` need a finance decision (a source
   of money) and are never paid automatically; `V1_PAID` orders whose escrow was never reduced show as `I3_LEGACY_ESCROW_NOT_REDUCED` (Low) and are
   reviewed, never adjusted. Fees of old orders are never recalculated.
5. Remove the V1 payout routes only when all of these hold: no unclassified V1 obligation remains, the legacy-data reconciliation was approved, and
   there were **no V1 payout calls for 14 days**. If a call happens, investigate before removing; a fixed date alone is not enough.

## Known limits

* Refunds after the payout are out of scope (task T30, handled manually).
* A process kill in the short window between a successful subsidy hold and saving the order leaves a held reservation for a sub-order that does
  not exist. It stays visible in the budget (reserved amount) and can be released by finance.
* The fee and shipping allocations are records in `OrderSettlements`; there is no platform wallet. Moving that money out is a finance process.
