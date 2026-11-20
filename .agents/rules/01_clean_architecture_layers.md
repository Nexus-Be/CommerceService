# 01 — CLEAN ARCHITECTURE & BOUNDED CONTEXT (COMMERCE SERVICE)

## 1. Bounded Context & Port Allocation
* Bounded Context: Shopping cart management, quote & order placement, multi-vendor sub-orders, auction orders, GHN shipping tracking, escrow settlement.
* Service Port: `8003` (HTTP REST API).
* Database: PostgreSQL `commerce_db` on port `5435`.

## 2. 4-Layer Clean Architecture
* `Commerce.Domain`: POCO Entities (`Cart`, `CartItem`, `Order`, `SubOrder`, `SubOrderItem`, `AuctionOrder`), Canonical Enums (`OrderStatus`, `SubOrderStatus`, `PaymentMethod`), Exceptions (`CommerceDomainException`).
* `Commerce.Application`: Use cases in `Services/{Feature}/`, interface `ICommerceDbContext`, request/response records, typed client interfaces (`IWalletClient`, `ICatalogClient`, `IFulfillmentClient`, `IUserClient`, `IAuctionClient`).
* `Commerce.Infrastructure`: `AppDbContext`, 16 EF Core configurations, 4 migrations, seeds, and typed REST clients (`WalletClient`, `CatalogClient`, `FulfillmentClient`, `UserClient`, `AuctionClient`).
* `Commerce.Api`: Thin controllers in `Controllers/Public/`, background workers (`PaymentTimeoutWorker`, `VnPayPaymentPollingWorker`), middlewares, extensions, composition root (`Program.cs`).

## 3. Feature Co-Location (Mandatory)
* `Services/Cart/`: `ICartService.cs`, `CartService.cs`, `CartRequests.cs`, `CartResponses.cs`, `CartDtos.cs`.
* `Services/Order/`: `IOrderService.cs`, `OrderService.cs`, `OrderRequests.cs`, `OrderResponses.cs`, `OrderDtos.cs`.
* `Services/AuctionOrder/`: `IAuctionOrderService.cs`, `AuctionOrderService.cs`, `AuctionOrderRequests.cs`, `AuctionOrderResponses.cs`.

## 4. Thin 1-to-1 Controller Pairing
* `CartsController.cs` -> injects `ICartService`.
* `OrdersController.cs` -> injects `IOrderService`.
* `AuctionOrdersController.cs` -> injects `IAuctionOrderService`.

## 5. Absolute Architectural Prohibitions
* 🛑 Prohibit Repository Pattern over EF Core (inject `ICommerceDbContext` directly).
* 🛑 Prohibit MediatR & CQRS handlers.
* 🛑 Prohibit Saga state machines, Outbox/Inbox tables, RabbitMQ, and MassTransit.
* 🛑 Prohibit direct queries to other microservice databases.
