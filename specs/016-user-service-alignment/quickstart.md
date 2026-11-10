# Quickstart Validation Guide: User Service Alignment

**Feature Directory**: `specs/016-user-service-alignment`  
**Phase**: Phase 1 (Quickstart & Validation Guide)  
**Status**: Completed  

---

## 1. Prerequisites & Environment Setup

Ensure the following environment variables are configured before running tests or launching the service:

```bash
# Commerce Service Port & Database
set COMMERCE_SERVICE_PORT=8003
set ASPNETCORE_ENVIRONMENT=Development
set COMMERCE_CONNECTION_STRING=Host=localhost;Port=5435;Database=commerce_db;Username=postgres;Password=MatkhaucuaTui123@;SSL Mode=Prefer;Trust Server Certificate=true

# Inter-Service URLs
set USER_SERVICE_URL=http://localhost:8001
set WALLET_SERVICE_URL=http://localhost:8008
set CATALOG_SERVICE_URL=http://localhost:8002
set FULFILLMENT_SERVICE_URL=http://localhost:8006
set INTERNAL_SERVICE_SECRET=eaT0Ombups70WJzd85CBBpp8rAno55SxKn1qvYBGH9e
```

---

## 2. Validation Scenario 1: Clean Solution Compilation

**Objective**: Verify that domain enums, client interfaces, and controller extensions compile cleanly with 0 errors and 0 warnings.

```bash
dotnet build Commerce.sln -c Release --no-restore -m:1 /p:UseSharedCompilation=false
```
* **Expected Outcome**: `Build succeeded. 0 Warning(s), 0 Error(s)`.

---

## 3. Validation Scenario 2: Buyer Permission Enforcement Gate

**Objective**: Verify that an inactive or restricted buyer is blocked immediately at checkout.

```bash
# Execute unit tests for buyer permission gate
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~OrderCheckoutTests.CreateOrder_ShouldThrowBuyerActionRestricted_WhenUserIsRestricted"
```
* **Expected Outcome**: Test verifies that `CheckBuyerPermissionAsync` returning `IsAllowed = false` causes `CreateOrderAsync` to throw `CommerceDomainException("BUYER_ACTION_RESTRICTED", ...)` with HTTP 403, and neither inventory reservation nor wallet payment is invoked.

---

## 4. Validation Scenario 3: Order Completion & Partner Reputation Adjustment

**Objective**: Verify that completing a master order or sub-order rewards the seller with +1 reputation.

```bash
# Execute unit tests for order completion flow
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~OrderLifecycleTests.CompleteOrder_ShouldRewardSellerReputation_WhenOrderDelivered"
```
* **Expected Outcome**: Test confirms that `CompleteOrderAsync` transitions `Order.Status` to `Completed`, updates sub-orders, and calls `_userClient.AdjustReputationAsync` with `scoreDelta: 1`, `reason: "OrderCompleted"`.

---

## 5. Validation Scenario 4: Seller Violation Cancellation Penalty

**Objective**: Verify that when a seller cancels a confirmed order without acceptable justification, 5 points are deducted.

```bash
# Execute unit tests for cancellation violation penalty
dotnet test tests/Commerce.UnitTests/Commerce.UnitTests.csproj --filter "FullyQualifiedName~OrderLifecycleTests.CancelOrder_ShouldPenalizeSeller_WhenCancelledWithoutReason"
```
* **Expected Outcome**: Test confirms that when seller initiates cancellation with `CancellationWithoutReason`, `_userClient.CreatePenaltyAsync` is called with `customScoreDeduction: 5` and `policyClauseCode: "POLICY-SEC-3.2"`.
