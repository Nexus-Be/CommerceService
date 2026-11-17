# Research & Technical Decisions: Fulfillment Delivery Synchronization and Sub-Order Lifecycle Governance

**Feature Branch**: `025-fulfillment-delivery-and-suborder-lifecycle`  
**Date**: 2026-11-17  
**Status**: Completed  
**Target Service**: `CommerceService` (Port 8003, Database `commerce_db:5435`)  

---

## 1. Webhook DTO Alignment with Fulfillment Service

### Problem Statement
FulfillmentService (Port 8006) captures logistics updates from Giao Hàng Nhanh (GHN), including Proof-of-Delivery (`pod_url`) and courier details (`shipper_name`, `shipper_phone`). Previously, `CommerceWebhookClient` sent inconsistent field names (`trackingNumber`, `description`, `timestamp`). Moreover, `UpdateDeliveryStatusRequest` in `Commerce.Application` lacked fields for POD URL and courier contact info, discarding valuable delivery evidence.

### Decision
1. **DTO Harmonization**:
   - Update `UpdateDeliveryStatusRequest` in `Commerce.Application/Services/Order/OrderRequests.cs`:
     ```csharp
     public sealed record UpdateDeliveryStatusRequest(
         string Status,
         DateTimeOffset? DeliveredAt = null,
         string? CarrierCode = null,
         string? TrackingCode = null,
         string? Note = null,
         string? Location = null,
         string? PodUrl = null,
         string? ShipperName = null,
         string? ShipperPhone = null);
     ```
2. **Order History Enrichment**:
   - Expand `FormatHistoryReason(...)` in `OrderService.cs` to format rich audit trails:
     `"[{Status}] {Note} - {Location} | Courier: {ShipperName} ({ShipperPhone}) | POD: {PodUrl}"`.
   - Update `SubOrder`: If `so.TrackingCode` or `so.CarrierCode` are empty or updated, overwrite them with verified carrier details.
3. **Internal Security Guard**:
   - Reaffirm strict verification of `X-Internal-Service-Key` in `OrdersController.cs` for endpoint `POST /api/v1/sub-orders/{subOrderId}/delivery-status`.

### Rationale
- Zero schema migration needed: POD and courier data are stored directly inside the audit trail (`OrderStatusHistory.Reason`) and exposed through the shipment timeline endpoint `GET /api/v1/sub-orders/{subOrderId}/shipments/timeline`.
- Complete contract parity between CommerceService and FulfillmentService.

---

## 2. Guarded Buyer Delivery Confirmation

### Problem Statement
In `OrderService.cs`:
- `BuyerConfirmSubOrderReceiptAsync` permitted confirmation when `so.Status == SubOrderStatus.Shipped || so.Status == SubOrderStatus.Delivered`.
- `CompleteOrderAsync` checked only `order.Status != Cancelled && order.Status != Expired`, completely failing to verify if packages had been delivered.
This introduced a critical financial flaw: buyers could prematurely confirm receipt while items were still in transit (`Shipped`), triggering premature escrow payout to sellers before goods were received.

### Decision
1. **Strict Sub-Order Guard**:
   - In `BuyerConfirmSubOrderReceiptAsync`, enforce:
     ```csharp
     if (so.Status != SubOrderStatus.Delivered)
     {
         throw new CommerceDomainException("INVALID_STATUS", "Sub-order cannot be confirmed until it is delivered.");
     }
     ```
2. **Strict Master Order Guard**:
   - In `CompleteOrderAsync`, verify that every non-cancelled sub-order has `Status == SubOrderStatus.Delivered`. If any active sub-order is not delivered, throw `CommerceDomainException("INVALID_STATUS", "Order cannot be completed until all active sub-orders are delivered.")`.

### Rationale
- Eliminates premature release of escrow funds.
- Matches industry e-commerce standards (Shopee, Lazada) where receipt confirmation is strictly gated on carrier delivery confirmation.

---

## 3. Independent Sub-Order Cancellation Architecture

### Problem Statement
When a checkout contains items from multiple shops (e.g. Shop A and Shop B), `OrdersController.CancelOrder` cancelled the entire order. If Shop A had already shipped (`Shipped`), the buyer was blocked from cancelling Shop B even if Shop B delayed fulfillment for days.

### Decision
1. **New API Endpoint**:
   - Add `POST /api/v1/sub-orders/{subOrderId}/cancel` in `OrdersController.cs` (or via `IOrderService.CancelSubOrderAsync`).
2. **Authorization & Status Rules**:
   - Verify caller ownership (`so.BuyerId == currentUserId`).
   - Allow cancellation ONLY when `so.Status` is `PendingPayment`, `Paid`, or `Confirmed`.
   - If `so.Status` is `Shipped`, `Delivered`, or `Completed`, throw `CommerceDomainException("SUB_ORDER_CANNOT_BE_CANCELLED", "Sub-order cannot be cancelled once shipped or completed.")`.
3. **Downstream Coordination**:
   - Release warehouse stock reservation for only the cancelled sub-order's SKUs via `IFulfillmentClient.ReleaseSubOrderStockAsync(so.OrderId, itemsToRelease, "BuyerCancelledSubOrder", ct)`.
   - If the sub-order was confirmed and has an active shipment (`so.TrackingCode` or `so.CarrierCode`), invoke `IFulfillmentClient.CancelShipmentAsync(so.Id, reason, ct)` targeting `POST /api/v1/shipments/{id}/cancel` on FulfillmentService.
   - If paid (`so.PaidAt.HasValue`), issue a 100% wallet refund for `so.TotalAmount` via `_walletClient.RefundSubOrderPaymentAsync(...)`.
   - Release voucher allocations for that sub-order via `ReleaseVoucherUsagesForSubOrderAsync`.
   - If all sibling sub-orders are cancelled, transition the parent `Order.Status = OrderStatus.Cancelled`.

### Rationale
- Isolates merchant risk: Non-violating sellers are unaffected by cancellations of sibling sub-orders.
- Leverages existing atomic stock release endpoints in FulfillmentService.

---

## 4. Background Worker Auto-Completion & Financial Settlement

### Problem Statement
70%–80% of e-commerce buyers never click "Confirm Receipt" after their parcel arrives. Without automated completion, seller payouts remain permanently trapped in platform escrow.

### Decision
1. **Lifecycle Worker Extension**:
   - In `PaymentTimeoutWorker.cs` (or unified `OrderLifecycleWorker`), add a periodic scan for eligible delivered sub-orders:
     ```csharp
     var threshold = DateTimeOffset.UtcNow.AddMinutes(-autoCompleteMinutes); // e.g., 3 days (prod) or 3 minutes (test)
     var deliveredSubOrders = await dbContext.SubOrders
         .Include(s => s.Items)
         .Where(s => s.Status == SubOrderStatus.Delivered && s.DeliveredAt.HasValue && s.DeliveredAt.Value <= threshold)
         .Take(50)
         .ToListAsync(ct);
     ```
2. **Automated Completion Pipeline**:
   - For each sub-order:
     - Set `so.Status = SubOrderStatus.Completed` and `so.CompletedAt = now`.
     - Settle seller escrow payout via `_walletClient.SettleSellerSubOrderAsync(...)` (unless COD).
     - Reward seller reputation (+1 point) via `_userClient.AdjustReputationAsync(...)`.
     - Sync purchase records to catalog for review eligibility via `_catalogClient.SyncPurchasesBatchAsync(...)`.
     - Record `OrderStatusHistory` with `ActorRole.System` and reason `"Auto-completed after delivery inspection window expired."`.
     - Sync parent master order if all siblings are completed.
3. **Configuration**:
   - Add `OrderLifecycle:AutoCompleteDeliveredMinutes` in `appsettings.json` (defaults to `4320` minutes = 3 days, easily overridden to `3` for evaluation).

### Rationale
- Guarantees financial predictability for merchants.
- Requires zero new background infrastructure (integrates seamlessly into existing hosted background service).

---

## 5. Cash on Delivery (COD) Settlement Bypass Invariant

### Problem Statement
For orders placed with `PaymentMethod.Cod`, the buyer pays physical cash directly to the courier upon delivery. Platform escrow never holds digital funds for this order. Calling `_walletClient.SettleSellerSubOrderAsync` on COD completion would mistakenly debit the platform's system wallet, creating an artificial negative cash balance.

### Decision
- In both manual confirmation (`BuyerConfirmSubOrderReceiptAsync`) and auto-completion workers:
  ```csharp
  if (so.PaymentMethod != PaymentMethod.Cod)
  {
      settled = await _walletClient.SettleSellerSubOrderAsync(...);
  }
  else
  {
      _logger.LogInformation("COD SubOrder '{SubOrderId}' completed. Courier reconciles physical cash directly.", so.Id);
  }
  ```

### Rationale
- Enforces strict financial accounting integrity: Digital wallet escrow is settled only when digital wallet funds were initially ingested.
