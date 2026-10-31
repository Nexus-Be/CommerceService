# 06 — DATABASE PERSISTENCE & MIGRATIONS (COMMERCE SERVICE)

## 1. Database Isolation
* Dedicated database: `commerce_db` on host port `5435`.
* Primary key: 100% `Guid` for entities (`Carts`, `CartItems`, `Orders`, `SubOrders`, `OrderItems`).
* Monetary values (`TotalAmount`, `Price`): explicit precision `HasPrecision(18, 2)`.

## 2. Entity Configuration & Soft Delete
* Every entity declares `IsDeleted` and `DeletedAt`. Enforce query filter: `builder.HasQueryFilter(x => !x.IsDeleted)`.
* Foreign keys configured with `DeleteBehavior.Restrict` or `DeleteBehavior.SetNull`.

## 3. Migration Safety
* No destructive migrations (`DROP TABLE`, `DROP COLUMN`) without explicit authorization.
* Startup safe auto-migration: `db.Database.Migrate()` wrapped in `try-catch` checking `db.Database.IsRelational()`.
