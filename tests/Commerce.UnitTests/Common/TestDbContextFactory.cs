namespace Commerce.UnitTests.Common;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Infrastructure.Persistence.Configurations;

public class TestCommerceDbContext : DbContext, ICommerceDbContext
{
    public TestCommerceDbContext(DbContextOptions<TestCommerceDbContext> options) : base(options)
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CartConfiguration).Assembly);
        modelBuilder.Entity<Cart>().Property(c => c.RowVersion).IsConcurrencyToken(false);
        modelBuilder.Entity<SubOrder>().Property(s => s.RowVersion).IsConcurrencyToken(false);
        modelBuilder.Entity<Order>().Property(o => o.RowVersion).IsConcurrencyToken(false);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<CartItem>())
        {
            if (entry.State == EntityState.Modified)
            {
                var id = entry.Entity.Id;
                var existsInDb = CartItems.AsNoTracking().Any(i => i.Id == id);
                if (!existsInDb)
                {
                    entry.State = EntityState.Added;
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<SubOrderItem>())
        {
            if (entry.State == EntityState.Modified)
            {
                var id = entry.Entity.Id;
                var existsInDb = SubOrderItems.AsNoTracking().Any(i => i.Id == id);
                if (!existsInDb)
                {
                    entry.State = EntityState.Added;
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

public static class TestDbContextFactory
{
    public static TestCommerceDbContext CreateInMemoryDbContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<TestCommerceDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        var context = new TestCommerceDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
