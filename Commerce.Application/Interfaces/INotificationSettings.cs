namespace Commerce.Application.Interfaces;

/// <summary>Whether notifications are on (Notification:Enabled). When off, nothing is queued.</summary>
public interface INotificationSettings
{
    bool Enabled { get; }
}
