namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Workers;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMMERCE_TEST_DB")))
            Skip = "Set COMMERCE_TEST_DB to a PostgreSQL connection string (a user that may create databases) to run this test.";
    }
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMMERCE_TEST_DB")))
            Skip = "Set COMMERCE_TEST_DB to a PostgreSQL connection string (a user that may create databases) to run this test.";
    }
}

/// <summary>
/// The worker that sends the notification outbox: what it sends and when, how it completes a row that waits for the shop id (the seller profile id,
/// never the seller's user id), when it retries, when it stops, and that two workers never send the same row. Real PostgreSQL (the claim is an
/// UPDATE with a condition), a fake Notification service and a fake User service.
/// </summary>
public sealed class NotificationOutboxWorkerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 3, 0, 0, TimeSpan.Zero);

    private string? _admin;
    private string? _database;
    private string _connection = string.Empty;
    private readonly Clock _clock = new(T0);
    private readonly FakeNotification _network = new();
    private readonly Mock<IUserClient> _users = new();

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeNotification : HttpMessageHandler, IHttpClientFactory
    {
        public readonly List<(string Path, string? UserId, string? Roles, string? Caller, string Body)> Requests = new();
        public Func<HttpResponseMessage> Answer { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);
        private readonly object _lock = new();

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("http://notification") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_lock)
            {
                Requests.Add((request.RequestUri!.AbsolutePath, Header(request, "x-user-id"), Header(request, "x-user-roles"), Header(request, "X-Caller-Service"), body));
            }

            return Answer();
        }

        private static string? Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    public NotificationOutboxWorkerTests()
    {
        _users.Setup(u => u.GetSellerShopAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SellerShopResult(false, null));
    }

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("COMMERCE_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin))
            return;

        _database = "commerce_outbox_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_admin))
        {
            await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin).ExecuteNonQueryAsync();
        }

        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using var db = NewDb();
        // Not MigrateAsync and not EnsureCreated: this service's migration chain and its seed data cannot build an empty database (FixSubOrderJsonSeed
        // deletes seeded sub-orders that discount usages still point to, and seeded invoices point to orders that are not seeded). That is an upstream
        // problem unrelated to the outbox. The tables are created from the model without the seed rows; a consistency test keeps the model equal to the snapshot.
        var script = db.Database.GenerateCreateScript();
        foreach (var statement in System.Text.RegularExpressions.Regex.Split(script, @";\s*?
"))
        {
            var sql = statement.Trim();
            if (sql.Length == 0 || sql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
                continue;
            await db.Database.ExecuteSqlRawAsync(sql);
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is null || _admin is null)
            return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE \"{_database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);

    private NotificationOutboxWorker NewWorker()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_connection));
        services.AddSingleton(_users.Object);
        var provider = services.BuildServiceProvider();
        return new NotificationOutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), _network,
            new CommerceNotificationOptions(true, "http://notification", new Guid("00000000-0000-0000-0000-0000000000c3")),
            _clock, NullLogger<NotificationOutboxWorker>.Instance);
    }

    private async Task<SubOrder> AddSubOrder(SubOrderStatus status = SubOrderStatus.Paid, Guid? shopId = null)
    {
        await using var db = NewDb();
        var order = new Order { Id = Guid.NewGuid(), BuyerId = Guid.NewGuid(), Status = OrderStatus.Paid, TotalAmount = 233000m, PaymentDeadlineAt = T0.AddMinutes(15) };
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = $"SUB-20261117-{Guid.NewGuid():N}"[..24], OrderId = order.Id, BuyerId = order.BuyerId, SellerId = Guid.NewGuid(),
            Status = status, PaymentMethod = PaymentMethod.Wallet, ContactPhone = "0901234567", PaymentDeadlineAt = T0.AddMinutes(15),
            PaidAt = T0.AddMinutes(-1), SellerRespondDeadlineAt = T0.AddHours(23), ShopId = shopId
        };
        order.SubOrders.Add(sub);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return sub;
    }

    private async Task<NotificationOutboxItem> AddRow(
        SubOrder sub, NotificationOutboxStatus status = NotificationOutboxStatus.AwaitingData, string? payload = null,
        DateTimeOffset? createdAt = null, DateTimeOffset? lockedUntil = null)
    {
        await using var db = NewDb();
        var created = createdAt ?? T0;
        var row = new NotificationOutboxItem
        {
            Id = Guid.NewGuid(), EventId = OrderPaidNotices.EventId(sub.Id), Kind = "order-payments", SubOrderId = sub.Id, SellerUserId = sub.SellerId,
            Status = status, PayloadJson = payload, NextRetryAt = created, GiveUpAt = created.AddHours(24), LockedUntil = lockedUntil,
            CreatedAt = created, UpdatedAt = created
        };
        db.NotificationOutbox.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private async Task<NotificationOutboxItem> Reload(Guid id)
    {
        await using var db = NewDb();
        return await db.NotificationOutbox.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("{\"detail\":\"d\"}") };

    // ------------------------------------------------------------------ completing and sending

    [PostgresFact]
    public async Task ARowWaitingForTheShop_IsCompletedWithTheSellerProfileId_FrozenAndSentWithTheServiceIdentity()
    {
        var sub = await AddSubOrder();
        var row = await AddRow(sub);
        var shop = Guid.NewGuid();
        _users.Setup(u => u.GetSellerShopAsync(sub.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(new SellerShopResult(true, shop));

        await NewWorker().RunOnceAsync(CancellationToken.None);

        var sent = _network.Requests.Should().ContainSingle().Subject;
        sent.Path.Should().Be("/api/v1/notifications/order-payments");
        sent.UserId.Should().Be("00000000-0000-0000-0000-0000000000c3");
        sent.Roles.Should().Be("service");
        sent.Caller.Should().Be("commerce-service");
        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        root.GetProperty("shopId").GetGuid().Should().Be(shop);
        root.GetProperty("shopId").GetGuid().Should().NotBe(sub.SellerId, "the shop id is the seller profile id, never the seller's user id");
        root.GetProperty("orderId").GetGuid().Should().Be(sub.Id);
        root.GetProperty("orderNo").GetString().Should().Be(sub.SubOrderNo);
        root.GetProperty("buyerUserId").GetGuid().Should().Be(sub.BuyerId);
        root.GetProperty("sellerUserId").GetGuid().Should().Be(sub.SellerId);
        root.GetProperty("currency").GetString().Should().Be("VND");
        root.GetProperty("sourceEventId").GetGuid().Should().Be(OrderPaidNotices.EventId(sub.Id));
        root.GetProperty("sellerAcceptDeadline").GetDateTimeOffset().Should().BeAfter(root.GetProperty("paidAt").GetDateTimeOffset());
        var after = await Reload(row.Id);
        after.Status.Should().Be(NotificationOutboxStatus.Sent);
        after.PayloadJson.Should().Be(sent.Body, "the frozen payload is what was sent");
        await using var db = NewDb();
        (await db.SubOrders.AsNoTracking().SingleAsync(s => s.Id == sub.Id)).ShopId.Should().Be(shop);
    }

    [PostgresFact]
    public async Task AKnownShopId_IsNotLookedUpAgain()
    {
        var shop = Guid.NewGuid();
        var sub = await AddSubOrder(shopId: shop);
        await AddRow(sub);

        await NewWorker().RunOnceAsync(CancellationToken.None);

        _users.Verify(u => u.GetSellerShopAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _network.Requests.Should().ContainSingle();
    }

    [PostgresFact]
    public async Task WhenTheUserServiceIsDown_TheRowWaitsWithoutAPayload_AndCompletesWhenItIsBack()
    {
        var sub = await AddSubOrder();
        var row = await AddRow(sub);
        var worker = NewWorker();
        _users.Setup(u => u.GetSellerShopAsync(sub.SellerId, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        await worker.RunOnceAsync(CancellationToken.None);
        var waiting = await Reload(row.Id);
        waiting.Status.Should().Be(NotificationOutboxStatus.AwaitingData);
        waiting.PayloadJson.Should().BeNull();
        waiting.LastError.Should().Be("SHOP_LOOKUP_TEMPORARILY_UNAVAILABLE");
        _network.Requests.Should().BeEmpty();

        _users.Setup(u => u.GetSellerShopAsync(sub.SellerId, It.IsAny<CancellationToken>())).ReturnsAsync(new SellerShopResult(true, Guid.NewGuid()));
        _clock.Now = T0.AddSeconds(20);
        await worker.RunOnceAsync(CancellationToken.None);

        _network.Requests.Should().ContainSingle();
        (await Reload(row.Id)).Status.Should().Be(NotificationOutboxStatus.Sent);
    }

    [PostgresFact]
    public async Task ASellerWithoutAShop_NeedsAttention_AndNothingIsSent_NotEvenWithTheUserId()
    {
        var sub = await AddSubOrder();
        var row = await AddRow(sub);

        await NewWorker().RunOnceAsync(CancellationToken.None);

        _network.Requests.Should().BeEmpty();
        var after = await Reload(row.Id);
        after.Status.Should().Be(NotificationOutboxStatus.NeedsAttention);
        after.LastError.Should().Be("SHOP_ID_UNRESOLVED");
    }

    [PostgresTheory]
    [InlineData(SubOrderStatus.Cancelled)]
    [InlineData(SubOrderStatus.PendingPayment)]
    public async Task ASubOrderThatIsNoLongerPaid_NeedsAttention_AndNothingIsSent(SubOrderStatus status)
    {
        var sub = await AddSubOrder(status);
        var row = await AddRow(sub);

        await NewWorker().RunOnceAsync(CancellationToken.None);

        _network.Requests.Should().BeEmpty();
        (await Reload(row.Id)).Status.Should().Be(NotificationOutboxStatus.NeedsAttention);
    }

    // ------------------------------------------------------------------ retries

    [PostgresFact]
    public async Task ATemporaryFailure_IsRetriedLater_WithTheSameEventAndPayload_UntilItWorks()
    {
        var sub = await AddSubOrder(shopId: Guid.NewGuid());
        var row = await AddRow(sub);
        var worker = NewWorker();
        _network.Answer = () => Status(HttpStatusCode.ServiceUnavailable);

        await worker.RunOnceAsync(CancellationToken.None);
        var failed = await Reload(row.Id);
        failed.Status.Should().Be(NotificationOutboxStatus.Pending);
        failed.NextRetryAt.Should().Be(T0.AddSeconds(15));
        (await worker.RunOnceAsync(CancellationToken.None)).Should().Be(0, "it is not due yet");

        _clock.Now = T0.AddSeconds(16);
        _network.Answer = () => Status(HttpStatusCode.OK);
        await worker.RunOnceAsync(CancellationToken.None);

        _network.Requests.Should().HaveCount(2);
        _network.Requests.Select(r => r.Body).Distinct().Should().ContainSingle("a retry carries exactly the first payload");
        (await Reload(row.Id)).Status.Should().Be(NotificationOutboxStatus.Sent);
    }

    [PostgresTheory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task APermanentRefusal_IsNotRetried_AndNeedsAttention(HttpStatusCode code)
    {
        var sub = await AddSubOrder(shopId: Guid.NewGuid());
        var row = await AddRow(sub);
        var worker = NewWorker();
        _network.Answer = () => Status(code);

        await worker.RunOnceAsync(CancellationToken.None);
        _clock.Now = T0.AddHours(2);
        await worker.RunOnceAsync(CancellationToken.None);

        _network.Requests.Should().ContainSingle();
        (await Reload(row.Id)).Status.Should().Be(NotificationOutboxStatus.NeedsAttention);
    }

    [PostgresFact]
    public async Task ARowThatKeepsFailingFor24Hours_NeedsAttention_AndIsNeverSentAgain()
    {
        var sub = await AddSubOrder(shopId: Guid.NewGuid());
        var row = await AddRow(sub, createdAt: T0.AddHours(-25));
        _network.Answer = () => Status(HttpStatusCode.ServiceUnavailable);

        await NewWorker().RunOnceAsync(CancellationToken.None);

        _network.Requests.Should().BeEmpty();
        (await Reload(row.Id)).Status.Should().Be(NotificationOutboxStatus.NeedsAttention);
    }

    // ------------------------------------------------------------------ several workers, crashes

    [PostgresFact]
    public async Task TwoWorkersAtTheSameTime_SendEveryRowExactlyOnce()
    {
        for (var i = 0; i < 10; i++)
            await AddRow(await AddSubOrder(shopId: Guid.NewGuid()));
        var first = NewWorker();
        var second = NewWorker();

        var results = await Task.WhenAll(first.RunOnceAsync(CancellationToken.None), second.RunOnceAsync(CancellationToken.None));

        results.Sum().Should().Be(10);
        _network.Requests.Should().HaveCount(10);
        _network.Requests.Select(r => r.Body).Distinct().Should().HaveCount(10);
        await using var db = NewDb();
        (await db.NotificationOutbox.CountAsync(x => x.Status == NotificationOutboxStatus.Sent)).Should().Be(10);
    }

    [PostgresFact]
    public async Task ARowLeasedByAWorkerThatDied_WaitsForTheLeaseToEnd_ThenIsSent()
    {
        var sub = await AddSubOrder(shopId: Guid.NewGuid());
        var row = await AddRow(sub, lockedUntil: T0.AddMinutes(2));
        var worker = NewWorker();

        (await worker.RunOnceAsync(CancellationToken.None)).Should().Be(0);
        _network.Requests.Should().BeEmpty();

        _clock.Now = T0.AddMinutes(3);
        (await worker.RunOnceAsync(CancellationToken.None)).Should().Be(1);
        (await Reload(row.Id)).Status.Should().Be(NotificationOutboxStatus.Sent);
    }

    [Fact]
    public void TheAnswerClasses_AreTheOnesTheContractDescribes()
    {
        NotificationOutboxWorker.Classify(200, null).Outcome.Should().Be(NotificationOutboxWorker.Outcome.Accepted);
        foreach (var temporary in new[] { 408, 429, 500, 502, 503, 504 })
            NotificationOutboxWorker.Classify(temporary, "x").Outcome.Should().Be(NotificationOutboxWorker.Outcome.Temporary);
        foreach (var permanent in new[] { 400, 401, 403, 404, 409, 422 })
            NotificationOutboxWorker.Classify(permanent, "x").Outcome.Should().Be(NotificationOutboxWorker.Outcome.Permanent);
        NotificationOutboxWorker.Backoff(1).Should().Be(TimeSpan.FromSeconds(15));
        NotificationOutboxWorker.Backoff(30).Should().Be(TimeSpan.FromMinutes(15));
    }
}
