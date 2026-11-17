namespace Commerce.Application.Services.Order;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;

/// <summary>
/// The "order paid" notification of a regular order, one per sub-order (each seller gets the part that is theirs, the buyer one notice per shop).
/// It is queued in the unit of work that stores the payment. The shop id has to be looked up first, so the row starts as AwaitingData and the
/// worker freezes the payload once it is complete.
/// </summary>
public static class OrderPaidNotices
{
    public const string Kind = "order-payments";

    /// <summary>The seller is asked to answer within a day. The notice says so as a reminder; nothing cancels the order when the day passes.</summary>
    public static readonly TimeSpan SellerAnswerWindow = TimeSpan.FromHours(24);

    /// <summary>The same sub-order always has the same event id, so queueing or sending it again is the same event for Notification.</summary>
    public static Guid EventId(Guid subOrderId) => new(MD5.HashData(Encoding.UTF8.GetBytes($"order-paid:{subOrderId:N}")));

    /// <summary>Queues the notice of a paid sub-order. Call it before the SaveChanges that stores the payment. Does nothing when notifications are off.</summary>
    public static void Enqueue(ICommerceDbContext db, INotificationSettings? settings, SubOrder subOrder)
    {
        if (settings is not { Enabled: true } || subOrder.PaidAt is null)
            return;

        var id = EventId(subOrder.Id);
        if (db.NotificationOutbox.Local.Any(x => x.EventId == id))
            return;

        var now = DateTimeOffset.UtcNow;
        db.NotificationOutbox.Add(new NotificationOutboxItem
        {
            Id = Guid.NewGuid(),
            EventId = id,
            Kind = Kind,
            SubOrderId = subOrder.Id,
            SellerUserId = subOrder.SellerId,
            Status = NotificationOutboxStatus.AwaitingData,
            NextRetryAt = now,
            GiveUpAt = now.AddHours(24),
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
