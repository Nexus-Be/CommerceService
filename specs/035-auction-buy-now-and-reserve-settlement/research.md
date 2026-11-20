# Research Findings: Auction Buy Now & Reserve Price Settlement Integration

**Feature**: `035-auction-buy-now-and-reserve-settlement`  
**Date**: 2026-11-20  
**Context**: Alignment with Auction Service v0.4 contract (`COMMERCE_HANDOFF_BUY_NOW_AND_RESERVE.md`)  

---

## 1. Technical Decisions & Research Topics

### Decision 1: Buy Now Settlement with 100% Held Deposit Model

- **Context**: In the Buy Now flow, when a buyer triggers "Buy Now", Auction Service locks 100% of the merchandise amount (`BuyNowPrice`) in Nexus Wallet (`HeldBalance`), split into either 2 holds (initial deposit + remaining balance) or 1 total hold. Auction Service pushes settlement to Commerce Service with `source: "BUY_NOW"`, `winningAmount: BuyNowPrice`, `depositAmount: BuyNowPrice`. How should Commerce Service process this order financially and state-wise?
- **Decision**:
  - Accept `source: "BUY_NOW"`, map to `AuctionOrderSource.BuyNow = 3`.
  - Calculate $\text{RemainingAmount} = \text{WinningAmount} - \text{DepositAmount} = 0\text{ VND}$.
  - Calculate $\text{PlatformFeeAmount} = \text{Math.Round}(\text{WinningAmount} \times 0.05, 2)$ and $\text{SellerReceiveAmount} = \text{WinningAmount} - \text{PlatformFeeAmount}$ (Seller receives 95% of merchandise price upon order fulfillment).
  - Initialize order status as `SubOrderStatus.PendingPayment` with `ShippingFeeAmount = null` and `TotalAmount = null`, setting `PaymentDeadlineAt = UtcNow.AddHours(24)`.
  - Do NOT auto-advance to `Paid`. The order represents physical merchandise requiring shipping logistics. The buyer must submit a delivery address (`PUT .../shipping`) within 24 hours.
  - When the buyer selects an address, calculate carrier shipping fee from GHN via Fulfillment Service, setting:
    $$\text{TotalAmount} = \text{RemainingAmount} (0\text{ VND}) + \text{ShippingFeeAmount} = \text{ShippingFeeAmount}$$
  - When the buyer pays (`POST .../pay`), debit only `TotalAmount` (the carrier shipping fee) from the buyer's Nexus Wallet balance.
- **Rationale**:
  - Treats the 100% held deposit as full merchandise prepayment without double-charging the buyer for the merchandise.
  - Ensures accurate carrier pricing based on real-world origin and destination warehouse coordinates.
  - Upon wallet payment of the shipping fee, Commerce dispatches callback `Paid` with `orderSource: "BUY_NOW"`, allowing Auction Service to execute capture on the held 100% deposit into Escrow.
- **Alternatives Considered**:
  - *Alternative A: Auto-transition order to `Paid` immediately on settlement ingestion.* Rejected because without a delivery address, shipping fee cannot be computed, carrier delivery orders cannot be registered with GHN, and shipping costs would be uncollected.
  - *Alternative B: Add shipping fee as an estimated flat fee on Auction Service.* Rejected because shipping fee depends strictly on the physical distance between the seller's warehouse and the buyer's delivery address, which is dynamically calculated by GHN through Fulfillment Service.

---

### Decision 2: Reserve Price Architectural Alignment & Zero-Deviation Handling

- **Context**: How should Commerce Service handle auctions configured with a reserve price (Giá sàn) when the auction closes?
- **Decision**:
  - When an auction closes below the reserve price, Auction Service sets the session status to `AwaitingSellerApproval` (12-hour seller review window) and **DOES NOT** push settlement to Commerce Service.
  - If the seller rejects or the 12-hour window expires, Auction Service cancels the auction and refunds bidder deposits; Commerce Service is never contacted.
  - If the seller approves the below-reserve bid, Auction Service ends the session and pushes settlement to Commerce Service with `source: "WINNER"`.
  - Commerce Service handles this order through standard winner settlement rules with zero code deviation, zero custom status flags, and zero schema changes.
- **Rationale**:
  - Bounded context separation: Reserve price approval is strictly an auction governance process owned by Auction Service.
  - Once the seller authorizes the sale, it is a legally binding contract identical to a standard winning bid.
- **Alternatives Considered**:
  - *Alternative A: Create a `ReservePrice` status in Commerce Service.* Rejected because Commerce Service should not manage pre-agreement auction negotiation states.

---

### Decision 3: Outbound Wire Value Standardization (`.ToWireValue()`)

- **Context**: In `AuctionOrderService` (e.g. `SellerAcceptAsync`, `SellerRejectAsync`) and `PaymentTimeoutWorker` (Seller expired loop), `OrderSource` was previously serialized using `.ToString().ToUpperInvariant()`. For `AuctionOrderSource.BuyNow` and `AuctionOrderSource.SecondChance`, this produces `"BUYNOW"` and `"SECONDCHANCE"` (without underscores), violating the agreed API contract with Auction Service which mandates snake_case (`"BUY_NOW"`, `"SECOND_CHANCE"`).
- **Decision**:
  - Mandate that all outbound callbacks, DTO factories, and background worker event payloads serialize `OrderSource` using the extension method `AuctionOrderSourceExtensions.ToWireValue()`.
  - Update `AuctionOrderSourceExtensions.ToWireValue()`:
    ```csharp
    public static string ToWireValue(this AuctionOrderSource source) =>
        source switch
        {
            AuctionOrderSource.SecondChance => "SECOND_CHANCE",
            AuctionOrderSource.BuyNow => "BUY_NOW",
            _ => "WINNER"
        };
    ```
  - Audit and replace all occurrences of `.ToString().ToUpperInvariant()` on `OrderSource` throughout the solution.
- **Rationale**:
  - Eliminates contract divergence and serialization bugs between microservices.
  - Guarantees strict JSON payload compliance for Auction Service event handlers.
- **Alternatives Considered**:
  - *Alternative A: Implement a custom System.Text.Json JsonConverter for AuctionOrderSource.* Viable, but extension method `.ToWireValue()` is already the established pattern in `Commerce.Domain` and provides direct, compile-time type-safety across DTO mapping and EF Core configurations.

---

### Decision 4: Buyer Timeout Penalty Ownership (Auction Service API 20 Ownership)

- **Context**: When a buyer fails to pay within 24 hours (`PaymentTimedOut`), who is responsible for applying the buyer reputation penalty?
- **Decision**:
  - In Commerce Service, `PaymentTimeoutWorker` cancels the order (`Status = Cancelled`) and dispatches the outbound callback `PaymentTimedOut` to Auction Service with `orderSource: "BUY_NOW"` (or `"WINNER"` / `"SECOND_CHANCE"`).
  - Commerce Service **DOES NOT** call `UserService` to penalize the buyer.
  - Auction Service, upon receiving the `PaymentTimedOut` callback, executes API 20 (`POST /api/v1/internal/users/{userId}/auction-violations`) to increment `ViolationStreak` and deduct reputation points, while also forfeiting the deposit hold.
- **Rationale**:
  - Prevents duplicate penalties: previously, if both services called `UserService`, the buyer would suffer double penalty points for a single default.
  - Aligns with the formal v0.4 handoff agreement where Auction Service owns auction violation management.
- **Alternatives Considered**:
  - *Alternative A: Commerce Service penalizes buyer and sends flag to Auction Service to skip penalty.* Rejected because Auction Service already tracks violation streaks and deposit forfeiture.
