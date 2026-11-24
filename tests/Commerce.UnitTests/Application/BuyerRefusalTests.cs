namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Delivery;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// T17, technical part: the carrier's reasons are kept per attempt, a returned parcel is classified only against an approved table, and the
/// penalty is stored before it is sent so a stop of Commerce or User never loses it. With no approved table nobody is penalised.
/// </summary>
public sealed class BuyerRefusalTests : IDisposable
{
    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Mock<IUserClient> _users = new();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    public void Dispose() => _db.Dispose();

    private static ConfiguredDeliveryFailurePolicy Policy(string? codes, string? delta = "-5") => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeliveryFailure:BuyerRefusedReasonCodes"] = codes,
            ["DeliveryFailure:BuyerRefusalScoreDelta"] = delta,
            ["DeliveryFailure:TableVersion"] = "test-1"
        }).Build());

    private OrderService Orders(IDeliveryFailurePolicy? policy) => new(
        _db, Mock.Of<IWalletClient>(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult(WalletRefundResult.Success())),
        Mock.Of<IFulfillmentClient>(f => f.ReleaseSubOrderStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)
            && f.ReleaseSubOrderStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)),
        Mock.Of<ICatalogClient>(), _users.Object, _cache, NullLogger<OrderService>.Instance,
        null, null, null, null, policy);

    private async Task<SubOrder> SeedShippedAsync()
    {
        var orderId = Guid.NewGuid();
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = "SUB-" + Guid.NewGuid().ToString("N")[..8], OrderId = orderId, BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Shipped, PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("A", "0901234567", "1 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"), ContactPhone = "0901234567",
            ItemsAmount = 220000m, ShippingFeeAmount = 30000m, TotalAmount = 250000m, PaymentDeadlineAt = DateTimeOffset.UtcNow, PaidAt = DateTimeOffset.UtcNow
        };
        var order = new Order { Id = orderId, BuyerId = sub.BuyerId, Status = OrderStatus.Paid, TotalAmount = 250000m, PaymentDeadlineAt = DateTimeOffset.UtcNow, PaymentAttemptCount = 1 };
        order.SubOrders.Add(sub);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return sub;
    }

    private static UpdateDeliveryStatusRequest Failed(int minutesAgo, string? code, string? reason = null) =>
        new("DeliveryAttemptFailed", DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), "GHN", "T1", "failed", ReasonCode: code, Reason: reason);

    private static UpdateDeliveryStatusRequest Returned(string? code = null) =>
        new("Returned", DateTimeOffset.UtcNow, "GHN", "T1", "returned", ReasonCode: code);

    private async Task<SubOrder> Reload(Guid id) => await _db.SubOrders.AsNoTracking().FirstAsync(item => item.Id == id);

    // ------------------------------------------------------------------ classifier

    [Theory]
    [InlineData("102,102,102", null, "BuyerRefused")]
    [InlineData("102", "102", "BuyerRefused")]
    [InlineData("", "102", "BuyerRefused")]            // the carrier sent the reason only with the return
    [InlineData("102,-,102", null, "Unknown")]         // one attempt came without a reason
    [InlineData("102,103,102", null, "Unknown")]       // a mixed history is not a clear refusal
    [InlineData("103", null, "Unknown")]               // not in the table
    [InlineData("102", "103", "Unknown")]              // the return says something else
    [InlineData("", null, "Unknown")]                  // no evidence at all
    [InlineData(null, null, "Unknown")]
    public void Classification_needs_clear_and_consistent_evidence(string? attempts, string? returned, string expected)
    {
        DeliveryFailureClassifier.Classify(attempts, returned, Policy("102")).Should().Be(expected);
    }

    [Fact]
    public void Appending_keeps_the_order_marks_missing_codes_and_never_breaks_the_list()
    {
        var stored = DeliveryFailureClassifier.Append(null, "102");
        stored = DeliveryFailureClassifier.Append(stored, null);
        stored = DeliveryFailureClassifier.Append(stored, "1,03");

        stored.Should().Be("102,-,103");
        DeliveryFailureClassifier.ParseAttempts(stored).Should().Equal("102", "-", "103");
    }

    [Fact]
    public void The_table_is_off_until_a_code_and_a_negative_penalty_are_both_configured()
    {
        Policy(null).Enabled.Should().BeFalse();
        Policy("102", null).Enabled.Should().BeFalse();
        Policy("102", "0").Enabled.Should().BeFalse();
        Policy("102", "7").Enabled.Should().BeFalse();       // a positive number is never a penalty
        Policy("102,103", "-5").Enabled.Should().BeTrue();
        Policy("102,103", "-5").IsBuyerRefusedCode(" 103 ").Should().BeTrue();
        Policy("102", "-500").BuyerRefusalScoreDelta.Should().Be(-50);
    }

    // ------------------------------------------------------------------ evidence and decision

    [Fact]
    public async Task Every_failed_attempt_keeps_its_reason_even_when_no_table_is_approved()
    {
        var sub = await SeedShippedAsync();
        var service = Orders(policy: null);

        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Failed(30, "102", "Customer refused"), default);
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Failed(20, null), default);

        var saved = await Reload(sub.Id);
        saved.DeliveryFailureCodes.Should().Be("102,-");
        saved.LastDeliveryFailureReason.Should().BeNull();       // the newest attempt had no text
        saved.LastDeliveryFailureAt.Should().NotBeNull();
    }

    [Fact]
    public async Task With_no_approved_table_a_return_penalises_nobody()
    {
        var sub = await SeedShippedAsync();
        var service = Orders(policy: Policy(codes: null));
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Failed(30, "102"), default);
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Failed(20, "102"), default);
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Failed(10, "102"), default);

        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Returned(), default);

        var saved = await Reload(sub.Id);
        saved.Status.Should().Be(SubOrderStatus.Cancelled);
        saved.ReturnClassification.Should().BeNull();
        saved.BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.None);
        saved.DeliveryFailureCodes.Should().Be("102,102,102");
    }

    [Fact]
    public async Task A_return_after_attempts_that_all_carry_an_approved_code_queues_the_penalty()
    {
        var sub = await SeedShippedAsync();
        var service = Orders(Policy("102"));
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Failed(30, "102"), default);
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Failed(20, "102"), default);

        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Returned(), default);

        var saved = await Reload(sub.Id);
        saved.ReturnClassification.Should().Be("BuyerRefused");
        saved.BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Pending);
        _users.Verify(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>()), Times.Never);   // queued, not sent in the request
    }

    [Theory]
    [InlineData("102", "103")]       // mixed reasons
    [InlineData("102", null)]        // one attempt without a reason
    [InlineData("999", "999")]       // not in the table
    public async Task Mixed_missing_or_unapproved_reasons_are_Unknown_and_cost_nothing(string? first, string? second)
    {
        var sub = await SeedShippedAsync();
        var service = Orders(Policy("102"));
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Failed(30, first), default);
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Failed(20, second), default);

        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Returned(), default);

        var saved = await Reload(sub.Id);
        saved.ReturnClassification.Should().Be("Unknown");
        saved.BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.None);
    }

    [Fact]
    public async Task A_repeated_return_event_does_not_queue_a_second_penalty()
    {
        var sub = await SeedShippedAsync();
        var service = Orders(Policy("102"));
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Failed(30, "102"), default);
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Returned(), default);
        await service.UpdateSubOrderDeliveryStatusAsync(sub.Id, Returned(), default);

        (await Reload(sub.Id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Pending);
    }

    // ------------------------------------------------------------------ penalty delivery

    private async Task<SubOrder> SeedPendingAsync()
    {
        var sub = await SeedShippedAsync();
        var tracked = await _db.SubOrders.FirstAsync(item => item.Id == sub.Id);
        tracked.Status = SubOrderStatus.Cancelled;
        tracked.DeliveryFailureCodes = "102,102";
        tracked.ReturnClassification = "BuyerRefused";
        tracked.BuyerRefusalPenaltyStatus = BuyerRefusalPenaltyStatus.Pending;
        await _db.SaveChangesAsync();
        return tracked;
    }

    private BuyerRefusalPenaltyService Penalty(IDeliveryFailurePolicy policy) => new(_db, _users.Object, policy, NullLogger<BuyerRefusalPenaltyService>.Instance);

    private static ReputationAdjustResponse Ok() => new(Guid.NewGuid(), "Buyer", false, false, 80, 75, -5, "Standard");

    [Fact]
    public async Task The_worker_sends_one_penalty_per_sub_order_with_the_sub_order_as_the_reference_and_then_stops()
    {
        var sub = await SeedPendingAsync();
        _users.Setup(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Ok());
        var service = Penalty(Policy("102"));

        (await service.RetryPendingAsync(50, default)).Should().Be(1);
        (await service.RetryPendingAsync(50, default)).Should().Be(0);      // Applied rows are never picked up again

        _users.Verify(u => u.AdjustReputationAsync(It.Is<ReputationAdjustRequest>(r =>
            r.UserId == sub.BuyerId && r.TargetRole == "Buyer" && r.ScoreDelta == -5 && r.Reason == "BuyerRefusedDelivery"
            && r.ReferenceType == "Order" && r.ReferenceId == sub.Id.ToString()), It.IsAny<CancellationToken>()), Times.Once);
        var saved = await Reload(sub.Id);
        saved.BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Applied);
        saved.BuyerRefusalPenaltyAppliedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task When_user_is_down_the_decision_is_kept_and_the_next_pass_delivers_it()
    {
        var sub = await SeedPendingAsync();
        var service = Penalty(Policy("102"));
        _users.SetupSequence(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReputationAdjustResponse?)null)
            .ThrowsAsync(new InvalidOperationException("User is down"))
            .ReturnsAsync(Ok());

        (await service.RetryPendingAsync(50, default)).Should().Be(0);
        (await Reload(sub.Id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Pending);
        (await service.RetryPendingAsync(50, default)).Should().Be(0);
        (await Reload(sub.Id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Pending);
        (await service.RetryPendingAsync(50, default)).Should().Be(1);

        var saved = await Reload(sub.Id);
        saved.BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Applied);
        saved.BuyerRefusalPenaltyAttempts.Should().Be(3);
    }

    [Fact]
    public async Task A_duplicate_answer_from_user_counts_as_delivered_because_the_penalty_already_exists()
    {
        var sub = await SeedPendingAsync();
        _users.Setup(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReputationAdjustResponse(Guid.NewGuid(), "Buyer", true, false, 75, 75, 0, "Standard"));

        (await Penalty(Policy("102")).RetryPendingAsync(50, default)).Should().Be(1);

        (await Reload(sub.Id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Applied);
    }

    [Fact]
    public async Task If_the_rules_are_switched_off_after_the_decision_nothing_is_sent_and_the_penalty_stays_visible_as_pending()
    {
        var sub = await SeedPendingAsync();

        (await Penalty(Policy(codes: null)).RetryPendingAsync(50, default)).Should().Be(0);

        _users.Verify(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        (await Reload(sub.Id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Pending);
    }
}
