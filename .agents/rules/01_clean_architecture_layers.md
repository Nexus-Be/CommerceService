# 01 — CLEAN ARCHITECTURE & BOUNDED CONTEXT (COMMERCE SERVICE)

## 1. Bounded Context & Port Allocation
* Bounded Context: Shopping cart management, order placement, sub-order breakdown, order lifecycle tracking.
* Service Port: `8003` (HTTP REST API).
* Database: PostgreSQL `commerce_db` on port `5435`.

## 2. 4-Layer Clean Architecture
* `Commerce.Domain`: Entities (`Cart`, `CartItem`, `Order`, `SubOrder`, `OrderItem`), Enums (`OrderStatus`, `PaymentStatus`), Exceptions (`CommerceDomainException`).
* `Commerce.Application`: Use cases in `Services/{Feature}/`, interface `ICommerceDbContext`, request/response records, typed client interfaces (`Interfaces/IWalletClient.cs`, `ICatalogClient.cs`).
* `Commerce.Infrastructure`: `AppDbContext`, EF Core configurations, migrations, and typed REST client `WalletClient`.
* `Commerce.Api`: Thin controllers in `Controllers/Public/` and `Controllers/Internal/`, middlewares, extensions, composition root (`Program.cs`).

## 3. Feature Co-Location (Mandatory)
* `Services/Cart/`: `ICartService.cs`, `CartService.cs`, `CartRequests.cs`, `CartResponses.cs`.
* `Services/Order/`: `IOrderService.cs`, `OrderService.cs`, `OrderRequests.cs`, `OrderResponses.cs`.

## 4. Thin 1-to-1 Controller Pairing
* `CartsController.cs` -> injects `ICartService`.
* `OrdersController.cs` -> injects `IOrderService`.

## 5. Absolute Architectural Prohibitions
* 🛑 Prohibit Repository Pattern over EF Core (inject `ICommerceDbContext` directly).
* 🛑 Prohibit MediatR & CQRS handlers.
* 🛑 Prohibit Saga state machines, Outbox/Inbox tables, RabbitMQ, and MassTransit.
* 🛑 Prohibit direct queries to other microservice databases.
