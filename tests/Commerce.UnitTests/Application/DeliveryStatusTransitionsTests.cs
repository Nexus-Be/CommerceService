namespace Commerce.UnitTests.Application;

using Commerce.Application.Services.Order;
using Commerce.Domain.Enums;
using FluentAssertions;
using Xunit;

public sealed class DeliveryStatusTransitionsTests
{
    [Theory]
    [InlineData(SubOrderStatus.Confirmed, "InTransit", true)]
    [InlineData(SubOrderStatus.Shipped, "OutForDelivery", true)]
    [InlineData(SubOrderStatus.Shipped, "DeliveryAttemptFailed", true)]
    [InlineData(SubOrderStatus.Confirmed, "Delivered", true)]
    [InlineData(SubOrderStatus.Shipped, "Delivered", true)]
    [InlineData(SubOrderStatus.Shipped, "Returned", true)]
    [InlineData(SubOrderStatus.Delivered, "Returned", true)]
    [InlineData(SubOrderStatus.Paid, "Picking", true)]
    // a late or replayed event must never undo progress
    [InlineData(SubOrderStatus.Completed, "InTransit", false)]
    [InlineData(SubOrderStatus.Delivered, "InTransit", false)]
    [InlineData(SubOrderStatus.Delivered, "Delivered", false)]
    [InlineData(SubOrderStatus.Completed, "Delivered", false)]
    [InlineData(SubOrderStatus.Shipped, "Picking", false)]
    [InlineData(SubOrderStatus.Cancelled, "InTransit", false)]
    [InlineData(SubOrderStatus.Cancelled, "Delivered", false)]
    [InlineData(SubOrderStatus.Refunded, "Returned", false)]
    [InlineData(SubOrderStatus.Completed, "Returned", false)]
    [InlineData(SubOrderStatus.PendingPayment, "Delivered", false)]
    public void IsAllowed_OnlyAcceptsForwardProgress(SubOrderStatus current, string carrierStatus, bool expected)
    {
        DeliveryStatusTransitions.IsAllowed(current, carrierStatus).Should().Be(expected);
    }

    [Fact]
    public void IsAllowed_IsCaseInsensitive()
    {
        DeliveryStatusTransitions.IsAllowed(SubOrderStatus.Completed, "intransit").Should().BeFalse();
        DeliveryStatusTransitions.IsAllowed(SubOrderStatus.Confirmed, "DELIVERED").Should().BeTrue();
    }
}
