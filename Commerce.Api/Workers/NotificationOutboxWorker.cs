namespace Commerce.Api.Workers;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>Where the notifications go and how this service introduces itself (Notification:Enabled, BaseUrl, ServiceUserId).</summary>
public sealed record CommerceNotificationOptions(bool Enabled, string BaseUrl, Guid ServiceUserId) : INotificationSettings
{
    public static readonly Guid DefaultServiceUserId = new("00000000-0000-0000-0000-0000000000c3");

    public static CommerceNotificationOptions FromConfiguration(IConfiguration configuration)
    {
        var enabled = bool.TryParse(configuration["Notification:Enabled"], out var parsed) && parsed;
        var baseUrl = Environment.GetEnvironmentVariable("NOTIFICATION_SERVICE_URL") ?? configuration["Notification:BaseUrl"];
        var serviceUser = Guid.TryParse(configuration["Notification:ServiceUserId"], out var id) && id != Guid.Empty ? id : DefaultServiceUserId;
        return new CommerceNotificationOptions(enabled, string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:8007" : baseUrl.Trim(), serviceUser);
    }
}

/// <summary>
/// Sends the notifications saved in the notification outbox (see <see cref="OrderPaidNotices"/>). A row is picked with a lease (an atomic
/// conditional UPDATE), so several instances never send the same row. A row still waiting for the shop id is completed first and only a complete
/// payload is frozen and sent: the shop id is the seller profile id from the User service, never the seller's user id. A temporary failure
/// (timeout, 408, 429, 5xx, User service down) is retried with growing delays and always with the same event id and payload; a permanent one
/// (a 4xx answer, a seller without a shop, a sub-order that was cancelled) is not retried and the row becomes NeedsAttention. A row that has not
/// worked 24 hours after it was created also becomes NeedsAttention. Notification treats a repeated event id as the same event.
/// </summary>
public sealed class NotificationOutboxWorker(
    IServiceScopeFactory scopes, IHttpClientFactory httpFactory, CommerceNotificationOptions options, TimeProvider clock,
    ILogger<NotificationOutboxWorker> logger) : BackgroundService
{
    public const string HttpClientName = "notification-service";
    private const int BatchSize = 20;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public enum Outcome { Accepted, Temporary, Permanent }

    public sealed record Attempt(Outcome Outcome, string? Error);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Notifications are disabled (Notification:Enabled is false).");
            return;
        }

        logger.LogInformation("Notification outbox worker started. Notification service: {BaseUrl}.", options.BaseUrl);
        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                handled = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The notification outbox scan failed; it will run again.");
            }

            try
            {
                await Task.Delay(handled > 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One scan: gives up rows that are too old, then claims and handles the rows that are due. Returns how many rows it handled.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();

        await db.NotificationOutbox
            .Where(x => (x.Status == NotificationOutboxStatus.Pending || x.Status == NotificationOutboxStatus.AwaitingData) && x.GiveUpAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, NotificationOutboxStatus.NeedsAttention)
                .SetProperty(x => x.LastError, x => x.LastError ?? "GAVE_UP_AFTER_24H")
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);

        var due = await db.NotificationOutbox.AsNoTracking()
            .Where(x => (x.Status == NotificationOutboxStatus.Pending || x.Status == NotificationOutboxStatus.AwaitingData)
                        && x.NextRetryAt <= now && (x.LockedUntil == null || x.LockedUntil < now))
            .OrderBy(x => x.NextRetryAt)
            .Select(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var handled = 0;
        foreach (var id in due)
        {
            // Atomic claim: only one instance gets the row; the lease frees it again if this instance dies half way.
            var claimed = await db.NotificationOutbox
                .Where(x => x.Id == id
                            && (x.Status == NotificationOutboxStatus.Pending || x.Status == NotificationOutboxStatus.AwaitingData)
                            && x.NextRetryAt <= now && (x.LockedUntil == null || x.LockedUntil < now))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.LockedUntil, now.Add(Lease))
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), cancellationToken);
            if (claimed == 0)
                continue;

            db.ChangeTracker.Clear();
            var item = await db.NotificationOutbox.SingleAsync(x => x.Id == id, cancellationToken);
            await HandleAsync(scope.ServiceProvider, db, item, cancellationToken);
            handled++;
        }

        return handled;
    }

    private async Task HandleAsync(IServiceProvider services, AppDbContext db, NotificationOutboxItem item, CancellationToken cancellationToken)
    {
        try
        {
            if (item.Status == NotificationOutboxStatus.AwaitingData)
            {
                var completion = await CompleteAsync(services, db, item, cancellationToken);
                if (completion.Payload is null)
                {
                    await FinishAsync(db, item, completion.Permanent ? Outcome.Permanent : Outcome.Temporary, completion.Error, cancellationToken);
                    return;
                }

                item.PayloadJson = completion.Payload;
                item.Status = NotificationOutboxStatus.Pending;
                await db.SaveChangesAsync(cancellationToken);
            }

            var result = await SendAsync(item, cancellationToken);
            await FinishAsync(db, item, result.Outcome, result.Error, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Handling the {Kind} notification {EventId} failed.", item.Kind, item.EventId);
            await FinishAsync(db, item, Outcome.Temporary, ex.GetType().Name + ": " + ex.Message, cancellationToken);
        }
    }

    private async Task FinishAsync(AppDbContext db, NotificationOutboxItem item, Outcome outcome, string? error, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        item.LockedUntil = null;
        item.UpdatedAt = now;
        item.LastError = error is { Length: > 500 } ? error[..500] : error;

        switch (outcome)
        {
            case Outcome.Accepted:
                item.Status = NotificationOutboxStatus.Sent;
                item.SentAt = now;
                item.LastError = null;
                logger.LogInformation("The {Kind} notification {EventId} of sub-order {SubOrderId} was accepted by the Notification service.",
                    item.Kind, item.EventId, item.SubOrderId);
                break;

            case Outcome.Permanent:
                item.Status = NotificationOutboxStatus.NeedsAttention;
                logger.LogError("The {Kind} notification {EventId} of sub-order {SubOrderId} needs attention and will not be retried: {Error}",
                    item.Kind, item.EventId, item.SubOrderId, item.LastError);
                break;

            default:
                if (now >= item.GiveUpAt)
                {
                    item.Status = NotificationOutboxStatus.NeedsAttention;
                    item.LastError ??= "GAVE_UP_AFTER_24H";
                    logger.LogError("The {Kind} notification {EventId} of sub-order {SubOrderId} was given up after 24 hours: {Error}",
                        item.Kind, item.EventId, item.SubOrderId, item.LastError);
                }
                else
                {
                    item.NextRetryAt = now.Add(Backoff(item.AttemptCount));
                    logger.LogWarning("The {Kind} notification {EventId} of sub-order {SubOrderId} will be retried at {RetryAt} (attempt {Attempt}): {Error}",
                        item.Kind, item.EventId, item.SubOrderId, item.NextRetryAt, item.AttemptCount, item.LastError);
                }

                break;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>15 s, 30 s, 1 min, 2 min ... capped at 15 minutes.</summary>
    public static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(15d * Math.Pow(2, Math.Max(0, attempt - 1)), TimeSpan.FromMinutes(15).TotalSeconds));

    // ------------------------------------------------------------------ sending

    private async Task<Attempt> SendAsync(NotificationOutboxItem item, CancellationToken cancellationToken)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/notifications/" + item.Kind)
        {
            Content = new StringContent(item.PayloadJson ?? "{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-user-id", options.ServiceUserId.ToString());
        request.Headers.Add("x-user-roles", "service");
        request.Headers.Add("X-Caller-Service", "commerce-service");

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return Classify((int)response.StatusCode, response.IsSuccessStatusCode ? null : await ReadSnippetAsync(response, cancellationToken));
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return new Attempt(Outcome.Temporary, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>2xx accepted; 408, 429 and 5xx are temporary; every other answer is a permanent refusal of this exact request.</summary>
    public static Attempt Classify(int status, string? detail)
    {
        if (status >= 200 && status < 300)
            return new Attempt(Outcome.Accepted, null);
        if (status >= 500 || status == 408 || status == 429)
            return new Attempt(Outcome.Temporary, $"HTTP {status}: {detail}");
        return new Attempt(Outcome.Permanent, $"HTTP {status}: {detail}");
    }

    private static async Task<string> ReadSnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return text.Length <= 300 ? text : text[..300];
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            return string.Empty;
        }
    }

    // ------------------------------------------------------------------ completing a row that waits for the shop id

    private sealed record Completion(string? Payload, bool Permanent, string? Error);

    private async Task<Completion> CompleteAsync(IServiceProvider services, AppDbContext db, NotificationOutboxItem item, CancellationToken cancellationToken)
    {
        var subOrder = await db.SubOrders.SingleOrDefaultAsync(s => s.Id == item.SubOrderId, cancellationToken);
        if (subOrder is null || subOrder.PaidAt is null || subOrder.SellerRespondDeadlineAt is null)
            return new Completion(null, true, "SUBORDER_NOT_PAID");
        if (subOrder.Status == SubOrderStatus.Cancelled || subOrder.Status == SubOrderStatus.PendingPayment)
            return new Completion(null, true, "SUBORDER_NO_LONGER_PAID");

        // The shop id is the seller profile id. It is looked up once and kept on the sub-order; the seller's user id is never used in its place.
        var shopId = subOrder.ShopId;
        if (shopId is null)
        {
            var users = services.GetRequiredService<IUserClient>();
            SellerShopResult shop;
            try
            {
                shop = await users.GetSellerShopAsync(subOrder.SellerId, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
            {
                return new Completion(null, false, "SHOP_LOOKUP_TEMPORARILY_UNAVAILABLE");
            }

            if (!shop.Found || shop.ShopId is null)
                return new Completion(null, true, "SHOP_ID_UNRESOLVED");

            shopId = shop.ShopId;
            subOrder.ShopId = shopId;
            await db.SaveChangesAsync(cancellationToken);
        }

        var payload = new
        {
            sourceEventId = item.EventId,
            orderId = subOrder.Id,            // the sub-order id: it opens the seller's page directly and resolves to the parent order for the buyer
            shopId = shopId.Value,
            buyerUserId = subOrder.BuyerId,
            sellerUserId = subOrder.SellerId,
            orderNo = subOrder.SubOrderNo,
            totalAmount = subOrder.TotalAmount,
            currency = "VND",
            paidAt = subOrder.PaidAt.Value,
            sellerAcceptDeadline = subOrder.SellerRespondDeadlineAt.Value
        };
        return new Completion(JsonSerializer.Serialize(payload, Json), false, null);
    }
}
