namespace Commerce.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Commerce.Domain.Entities;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<CheckoutGroup> CheckoutGroups => Set<CheckoutGroup>();
    public DbSet<DiscountCode> DiscountCodes => Set<DiscountCode>();
    public DbSet<DiscountCodeScope> DiscountCodeScopes => Set<DiscountCodeScope>();
    public DbSet<DiscountCodeUsage> DiscountCodeUsages => Set<DiscountCodeUsage>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<AuctionOrder> AuctionOrders => Set<AuctionOrder>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<OrderInvoice> OrderInvoices => Set<OrderInvoice>();
    public DbSet<OrderCancellation> OrderCancellations => Set<OrderCancellation>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

public class CommerceDbContext : AppDbContext
{
    public CommerceDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}
