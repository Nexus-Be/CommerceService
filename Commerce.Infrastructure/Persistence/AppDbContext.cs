namespace Commerce.Infrastructure.Persistence;

using MassTransit;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;

public class AppDbContext : DbContext, ICommerceDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<SubOrder> SubOrders => Set<SubOrder>();
    public DbSet<SubOrderItem> SubOrderItems => Set<SubOrderItem>();
    public DbSet<DiscountCode> DiscountCodes => Set<DiscountCode>();
    public DbSet<DiscountCodeScope> DiscountCodeScopes => Set<DiscountCodeScope>();
    public DbSet<DiscountCodeUsage> DiscountCodeUsages => Set<DiscountCodeUsage>();
    public DbSet<AuctionOrder> AuctionOrders => Set<AuctionOrder>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<OrderInvoice> OrderInvoices => Set<OrderInvoice>();
    public DbSet<OrderCancellation> OrderCancellations => Set<OrderCancellation>();
    public DbSet<OrderRefund> OrderRefunds => Set<OrderRefund>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<DailyOrderSequence> DailyOrderSequences => Set<DailyOrderSequence>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        Seeds.CommerceSeedData.SeedAll(modelBuilder);

        // MassTransit Transactional Outbox entities
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
