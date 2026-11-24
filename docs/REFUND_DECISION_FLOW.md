# Refund decision flow (statuses, retries, wallet contract)

## Statuses of an `OrderRefund`

| Status | Value | Meaning | Who moves it on |
|---|---|---|---|
| `Requested` | 0 | **Waiting for a decision.** Only a buyer's request (`ReasonCode = BUYER_REQUEST`) is ever in this status. | A seller or staff approves (`WalletPending`) or rejects (`Rejected`) |
| `WalletPending` | 4 | **Decided, waiting for the wallet to confirm.** Cancellation, rejection, return, delivery failure, dispute and an approved buyer request are all decided when the refund row is created or approved. | The request that decided it, or the retry worker, once the wallet confirms (`Completed`) |
| `Completed` | 1 | The wallet confirmed the credit; `RefundTransactionId` holds the wallet transaction. | |
| `Failed` | 2 | The refund moved no money and must be looked at (for example `WALLET_REFUND_ALREADY_HELD_BY_ANOTHER_REFUND`). | A person |
| `Rejected` | 3 | A buyer's request was rejected. | |

The decision is stored **before** the wallet is asked. A crash or a refusal therefore leaves a `WalletPending` refund that the worker retries; it never leaves a decided refund that looks undecided.

## Retry worker (`PaymentTimeoutWorker.ReconcilePendingRefundsAsync`)

- Takes only `WalletPending` refunds, never `Requested`. (It used to take every `Requested` refund, so a buyer's request could be refunded before anyone approved it.)
- A refund is retried only when it was last touched more than `RefundRetryLease` (2 minutes) ago, so the request that decided it can finish first.
- A worker takes a refund with a conditional update (`WalletPending` and unchanged `UpdatedAt`), which succeeds for one caller only. Two workers never refund the same row (covered by the PostgreSQL tests; set `COMMERCE_TEST_DB`).
- An approved buyer request is finished like the approval finishes it: the sub-order becomes `Refunded`.
- `OrderRefund.RowVersion` is kept as it is but is **not** the concurrency mechanism: nothing increments it for refunds.

The auction refund worker (`AuctionFinancialRetryWorker`) takes `WalletPending` auction refunds the same way.

## Wallet contract for a sub-order refund

`POST /api/v1/wallets/sub-orders/{subOrderId}/refund` with `{ userId, amount, reason, parentOrderId }`.

- `parentOrderId` is **mandatory**. Without it the wallet answers `400 PARENT_ORDER_REQUIRED` and credits nothing: the wallet checks the payment and takes the money out of the parent escrow.
- The wallet keeps **one refund per sub-order id**. There is no idempotency key. A repeated call with the same amount answers the existing transaction with `replayed: true`; a different amount answers `409 REFUND_CONFLICT`.
- `WalletClient.RefundSubOrderPaymentAsync` returns a `WalletRefundResult` (`Succeeded`, `TransactionId`, `Replayed`, `ErrorCode`).
- `RefundOutcome.ApplyAsync` is the single place that decides when a refund counts as done: it stores `RefundTransactionId` and sets `Completed`, **except** when the wallet answered a replay whose transaction already belongs to a different refund; then this refund moved no money and becomes `Failed` (`WALLET_REFUND_ALREADY_HELD_BY_ANOTHER_REFUND`) and is left to a person.
- The refund routes are closed unless the wallet runs with `ReleaseFeatures:OrderRefundEnabled=true`. While they are closed the refunds stay `WalletPending` and are retried; nothing is lost and no money moves.

## Partial refunds

Because the wallet keeps one refund per sub-order, a sub-order that was already refunded in part is **never completed automatically**: the dispute path answers `ManualProcessingRequired` before it asks the wallet, and no second refund row is made.

## Existing data

Migration `20261124120000_ClassifyPendingRefunds` runs once before any worker: every `Requested` refund that is not a `BUYER_REQUEST` becomes `WalletPending`; buyer requests stay `Requested`. Idempotent.

## Known limitation

The cancellation, rejection and return paths store the refund row before they ask the wallet, but they still save the rest of the operation (sub-order status, history) afterwards. A crash between the wallet call and that final save leaves a stored `WalletPending` refund and a credit the wallet already made; the retry then receives that credit back as a replay and completes the refund once.
