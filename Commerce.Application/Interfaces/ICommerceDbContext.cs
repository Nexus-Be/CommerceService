namespace Commerce.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Domain.Entities;

public interface ICommerceDbContext
{
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<SubOrder> SubOrders { get; }
    DbSet<SubOrderItem> SubOrderItems { get; }
    DbSet<DiscountCode> DiscountCodes { get; }
    DbSet<DiscountCodeScope> DiscountCodeScopes { get; }
    DbSet<DiscountCodeUsage> DiscountCodeUsages { get; }
    DbSet<AuctionOrder> AuctionOrders { get; }
    DbSet<OrderStatusHistory> OrderStatusHistories { get; }
    DbSet<OrderInvoice> OrderInvoices { get; }
    DbSet<OrderCancellation> OrderCancellations { get; }
    DbSet<OrderRefund> OrderRefunds { get; }
    DbSet<SystemSetting> SystemSettings { get; }
    DbSet<DailyOrderSequence> DailyOrderSequences { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<IdempotencyKey> IdempotencyKeys { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
