# Buyer refusal of a delivery (T17): evidence, decision, penalty

Status: the mechanism is built and tested. **It penalises nobody until the project owner approves a reason-code table and a penalty size.**
Without both, parcels that come back are still cancelled, refunded and restocked exactly as before; only the carrier's reasons are kept.

## What is kept (always)
* Fulfillment sends the carrier's `reasonCode` and `reason` with each delivery status, exactly as GHN reported them (read from the stored
  webhook payload, so a retry sends the same thing). A missing or empty reason is `null`; nothing is guessed.
* On `DeliveryAttemptFailed` Commerce appends the code to `SubOrders.DeliveryFailureCodes` (`102,102,-`, where `-` is an attempt that came
  without a code) and keeps the text and time of the last attempt.

## What is decided (only with an approved table)
When a sub-order is `Returned`, and only if the table is configured, Commerce classifies it once, with the table in force at that moment:

| Evidence | Result |
|---|---|
| the return event carries a code in the table, and every failed attempt has a code in the table | `BuyerRefused` |
| a return event without a code, any attempt without a code, any code outside the table, mixed reasons, or no evidence at all | `Unknown` (no penalty) |

`BuyerRefused` sets `BuyerRefusalPenaltyStatus = Pending` in the same save that records the return. Nothing is sent inside the request.
A table approved later never penalises an older return.

## How the penalty is sent
`PaymentTimeoutWorker` calls `BuyerRefusalPenaltyService.RetryPendingAsync` on every pass. For each `Pending` sub-order it calls User
`reputation-adjust` with `Reason = BuyerRefusedDelivery`, `ReferenceType = Order`, `ReferenceId = <sub-order id>`, target role Buyer. User keeps
one log per (user, role, reference type, reference, reason) under a unique index, so repeating a call can never penalise twice; a duplicate
answer counts as delivered. If User is down the row stays `Pending` and is tried again; a warning is logged after 10 attempts.
If the table is switched off after the decision the row stays `Pending` (visible), nothing is sent.

## Configuration (all off by default)
| Key | Meaning |
|---|---|
| `DeliveryFailure:BuyerRefusedReasonCodes` | Approved GHN reason codes that mean a refusal. A JSON array or one comma separated value (`DeliveryFailure__BuyerRefusedReasonCodes=102,103`). |
| `DeliveryFailure:BuyerRefusalScoreDelta` | A negative whole number, at most -50. Zero or positive means disabled. |
| `DeliveryFailure:TableVersion` | A label written to the log and the penalty note. |

## Still to do before it can be switched on
* GHN's table of reason codes (which code is "the recipient refused"): take it from GHN sandbox/support, not from a guess.
* The owner approves the codes and the penalty size, and decides how a wrongly penalised buyer gets it undone.
* Auction orders that come back are not covered; this is for regular sub-orders.

## Withdrawing a penalty (admin only)
`POST /api/v1/admin/buyer-refusal-penalties/{subOrderId}/revoke` with `{ "reason": "..." }` (10 to 500 characters). Only an `admin` can call it; staff can record a complaint but cannot change a score.

| Penalty before | What happens |
|---|---|
| `Pending` (User never confirmed it) | The sub-order moves to `RevokePending` and User is told. User records the withdrawal for the sub-order even though it took nothing, so a penalty that is still on its way is not applied. The final status is `Cancelled`. |
| `Applied` | User gives back exactly the points the penalty log recorded (the score is limited to 0 to 100, so this can be less than the configured amount, even 0), once. The final status is `Revoked` (points given back) or `Cancelled` (nothing to give back). |
| `RevokePending`, `Revoked`, `Cancelled` | Nothing changes and nothing is sent again; the first decision (admin, reason, time) stands. |
| `None` | 409 `BUYER_REFUSAL_PENALTY_NOT_OWED`. |

The decision (admin, reason, time) is stored in one conditional update before User is called. If User cannot be reached the answer is `202` with `RevokePending` and the worker repeats the call every pass, whether or not the penalty rules are still enabled. A penalty being sent by the worker at the moment of the withdrawal can never overwrite it: the worker only turns a sub-order from `Pending` to `Applied`, and User refuses a penalty for a sub-order it has already withdrawn.

User side: `POST api/v1/internal/users/reputation-penalty-revoke`. It writes one `BuyerRefusalPenaltyRevoked` reputation log per sub-order (a unique index makes a second approval a no-op) and takes the same per-user lock as the penalty call. It does not use the positive-reward rules (the seven-day anti-farming rule), because it is not a reward.
