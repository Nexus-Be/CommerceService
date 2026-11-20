# USER SERVICE & COMMERCE SERVICE ALIGNMENT CONTRACT

> **Target Service**: `CommerceService` (Port `8003`) ↔ `UserService` (Port `8001`)  
> **Protocol**: Synchronous Typed REST over HTTP/JSON  
> **Status**: Ratified and Implemented  
> **Last Updated**: 2026-11-10  

---

## 1. Overview & Principles

`CommerceService` interacts with `UserService` via typed client `IUserClient` implemented in `UserClient.cs`:
1. **100% RESTful**: Zero message brokers (RabbitMQ / MassTransit decommissioned).
2. **Security**: Calls send the shared internal key header `X-Internal-Service-Key` and propagate caller identity via `x-user-id`.
3. **Distributed Tracing**: Standard W3C `traceparent` headers are propagated across calls.
4. **Resilience & Timeout**: Strict 10-second timeout per outbound HTTP request.

---

## 2. Inbound & Outbound Integration Points

### 2.1 Buyer Purchasing Permission Gate (`INT-01`)
- **Trigger**: During order placement `POST /api/v1/orders`.
- **Target Endpoint**: `GET http://user-service:8001/api/v1/internal/users/{buyerId}/permission-check?action=buy`
- **Logic**:
  - Account must be in `UserStatus.Active`.
  - Active restrictions must not contain `Buyer:Restriction` or `Account:BanAccount`.
- **Error Behavior**:
  - If prohibited: Returns HTTP 403 `BUYER_ACTION_RESTRICTED` with message `"User has an active restriction prohibiting purchase actions."`.
  - Execution stops immediately without inventory reservation or wallet payment.

### 2.2 Order Completion & Partner Reputation Rewards (`INT-07`)
- **Trigger**:
  - `POST /api/v1/orders/{orderId}/complete` (Master order completion).
  - `POST /api/v1/sub-orders/{subOrderId}/confirm-receipt` (Sub-order delivery confirmation).
- **Target Endpoint**: `POST http://user-service:8001/api/v1/internal/users/reputation-adjust`
- **Payload**:
  ```json
  {
    "userId": "<seller_guid>",
    "targetRole": "Seller",
    "partnerUserId": "<buyer_guid>",
    "scoreDelta": 1,
    "reason": "OrderCompleted",
    "referenceType": "Order",
    "referenceId": "ORD-YYYYMMDD-XXXXXX",
    "note": "Reward for successfully completed order ORD-YYYYMMDD-XXXXXX."
  }
  ```
- **Defense Mechanisms**:
  - **Idempotency**: Retrying with the same `referenceId` returns `isDuplicate: true, appliedDelta: 0`.
  - **7-Day Anti-Farming Cooldown**: Repeated transactions between the same pair within 7 days yield `cooldownSuppressed: true, appliedDelta: 0`.

### 2.3 Seller Cancellation Violation Penalty (`INT-06`)
- **Trigger**:
  - `POST /api/v1/orders/{orderId}/cancel` (Initiated by Seller).
  - `POST /api/v1/orders/{orderId}/reject` (Initiated by Shop).
- **Target Endpoint**: `POST http://user-service:8001/api/v1/internal/users/penalties`
- **Condition**: Sub-order was previously confirmed (`Confirmed`) or paid (`Paid`), and cancelled without force majeure (`CancellationWithoutReason = 7` or `SellerRejected = 3`).
- **Payload**:
  ```json
  {
    "userId": "<seller_guid>",
    "targetRole": "Seller",
    "violationType": "CancellationWithoutReason",
    "referenceType": "Order",
    "referenceId": "ORD-YYYYMMDD-XXXXXX",
    "reason": "Seller cancelled confirmed order ORD-YYYYMMDD-XXXXXX without acceptable explanation.",
    "customScoreDeduction": 5,
    "policyClauseCode": "POLICY-SEC-3.2"
  }
  ```
- **Effect**: Deducts 5 reputation points from the seller's score and records audit penalties.
