# Research Findings: Auction Deposit Ingestion, Runner-Up Settlement & Split Reversal

**Feature**: `022-auction-deposit-and-runner-up-settlement`  
**Date**: 2026-11-15  
**Context**: Alignment with Auction Service v0.4 contract (Option C: Nexus Wallet Balance Hold)  

---

## 1. Technical Decisions & Research Topics

### Decision 1: Deposit Ledger Model vs Net Cash Billing Offset

- **Context**: In v0.4, winning bidders deposit money via Nexus Wallet `HeldBalance` before bidding. At auction close, the deposit is captured into order escrow. How should Commerce Service represent this deposit in the `AuctionOrder` entity and financial ledger?
- **Decision**: Store `DepositAmount` (held deposit) and `RemainingAmount = WinningAmount - DepositAmount` as first-class decimal columns in `AuctionOrder`.
  - The gross value of the order remains `WinningAmount`.
  - Seller payout remains $95\%$ of `WinningAmount` (`WinningAmount - PlatformFeeAmount`).
  - Platform fee remains $5\%$ of `WinningAmount`.
  - When the buyer submits shipping, `TotalAmount` is calculated as $\text{RemainingAmount} + \text{ShippingFeeAmount}$.
- **Rationale**:
  - The deposit is not an additional fee or external voucher subsidy; it is part of the bid price already committed.
  - Charging `TotalAmount = RemainingAmount + ShippingFeeAmount` ensures the buyer is debited only for the unpaid portion, preventing double-billing.
  - When Wallet Service captures the deposit via W2 into the order's escrow, the total escrow in Wallet Service equals $\text{DepositAmount} + \text{TotalAmount} = \text{WinningAmount} + \text{ShippingFeeAmount}$, perfectly covering seller settlement ($95\%$), platform fee ($5\%$), and carrier shipping fee.
- **Alternatives Considered**:
  - *Alternative A: Represent deposit as a line-item discount.* Rejected because a deposit is not a discount; it is a prepayment. Using discount mechanisms would distort GMV, tax, and seller payout formulas.
  - *Alternative B: Zero out winning amount to remaining amount.* Rejected because the legal agreement, product valuation, and invoice must show the true winning bid amount (`WinningAmount`).

---

### Decision 2: Runner-Up (`SECOND_CHANCE`) Multi-Winner Idempotency Strategy

- **Context**: When the primary winner defaults within 24 hours, Auction Service invites the runner-up bidder and issues an order settlement with `source = "SECOND_CHANCE"`. How should Commerce handle order uniqueness and idempotency?
- **Decision**: Introduce enum `AuctionOrderSource` (`Winner = 1`, `SecondChance = 2`) and update composite idempotency check to evaluate `(AuctionId, OrderSource, BuyerId)`.
  - If a settlement arrives with `(AuctionId, OrderSource, BuyerId)` matching an existing record: Return existing order (200 OK idempotent).
  - If a settlement arrives with `source = "SECOND_CHANCE"` while an existing order for the same auction is NOT cancelled: Reject with HTTP 409 Conflict (`SETTLEMENT_CONFLICT`).
  - If the previous order is `Cancelled`: Allow creation of the `SECOND_CHANCE` order with `DepositAmount = 0`, `RemainingAmount = WinningAmount`, and a fresh 24-hour `PaymentDeadlineAt`.
- **Rationale**:
  - Complies with Section 5 of document 18 (`18_POST_auction-order-settlement.md`).
  - Guarantees that only one active order exists per auction at any time, while preserving historical audit logs of the defaulted winner.
- **Alternatives Considered**:
  - *Alternative A: Overwrite the existing order with runner-up info.* Rejected because it destroys audit trails, cancellation histories, and buyer penalty linkage for the defaulted winner.
  - *Alternative B: Generate new unique AuctionId for runner-up.* Rejected because Auction Service tracks a single `AuctionId` across its entire lifecycle.

---

### Decision 3: Independent Reversal Split on Seller Default / Cancellation

- **Context**: When a seller rejects a paid auction order (`SellerRejectAsync`) or fails to respond within 24 hours (`PaymentTimeoutWorker`), who is responsible for refunding the buyer's payment and deposit?
- **Decision**: Enforce strictly independent reversals:
  - **Commerce Service** is solely responsible for refunding `auctionOrder.TotalAmount` (the amount the buyer transferred to Commerce via `_walletClient.PayOrderAsync`).
  - **Auction Service** is solely responsible for refunding the captured deposit (`DepositAmount`) via Wallet W5 (`POST /internal/wallets/holds/{holdId}/refund-captured`).
  - Commerce Service **MUST NOT** refund `DepositAmount`.
- **Rationale**:
  - Solves Section 7 of document 18: "Hai khoản hoàn độc lập, mỗi bên tự làm và tự idempotent".
  - If Commerce attempted to refund `TotalAmount + DepositAmount`, the buyer would receive duplicate deposit funds once Auction Service executed W5.
- **Alternatives Considered**:
  - *Alternative A: Commerce refunds 100% (including deposit) and notifies Auction to cancel W5.* Rejected because Auction Service and Wallet Service manage the deposit hold lifecycle; Commerce does not hold authorization tokens for the deposit hold.

---

### Decision 4: Enriched Outbound Callback Contract with `orderSource`

- **Context**: In document 10 (`10_CALLBACK_order-outcome.md`), Auction Service requires an additional property `orderSource` (`"WINNER"` or `"SECOND_CHANCE"`) in the callback `POST /api/v1/auctions/{auctionId}/settlement/payment-status`.
- **Decision**: Update `AuctionPaymentStatusCallbackRequest` in `Commerce.Application/Interfaces/IAuctionClient.cs` and `AuctionClient.cs` to include `string? OrderSource = null`.
  - In `PayAuctionOrderAsync`: populate `orderSource = auctionOrder.OrderSource.ToString().ToUpperInvariant()`.
  - In `SellerAcceptAsync` and `SellerRejectAsync`: populate `orderSource = auctionOrder.OrderSource.ToString().ToUpperInvariant()`.
  - In `PaymentTimeoutWorker`: populate `orderSource = auctionOrder.OrderSource.ToString().ToUpperInvariant()` for both payment timeout and seller expired loops.
- **Rationale**:
  - Auction Service uses `orderSource` to determine whether a payment timeout should forfeit the deposit and trigger runner-up invitation (`WINNER`) or simply close the auction and unlock the product without forfeiture (`SECOND_CHANCE`).
- **Alternatives Considered**:
  - *Alternative A: Send separate webhook endpoints for winner vs runner-up.* Rejected because Auction Service explicitly requested reusing the existing endpoint to minimize routing overhead.

---

### Decision 5: Non-Interference with Catalog Product Lock Lifecycle

- **Context**: When an auction order is cancelled due to payment timeout, should Commerce Service call Catalog Service to unlock product inventory?
- **Decision**: Commerce Service MUST NOT call Catalog Service when an auction order is cancelled. Catalog product unlocking is strictly owned and orchestrated by Auction Service upon receiving the `PaymentTimedOut` callback.
- **Rationale**:
  - Section 6 of document 10 specifies: "Commerce không tự nhả/khóa sản phẩm cho order đấu giá khi PaymentTimedOut. Auction quyết định: có người về nhì thì giữ sản phẩm cho order của họ, không thì nhả ở Catalog."
  - If Commerce prematurely called Catalog unlock, the product would become available for general purchase before Auction Service could reserve it for the runner-up bidder.
