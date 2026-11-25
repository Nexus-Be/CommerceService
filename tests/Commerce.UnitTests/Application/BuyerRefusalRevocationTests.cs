namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Delivery;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// The withdrawal of a buyer refusal penalty. An admin decides once; the decision is stored before User is called; the worker that sends a
/// penalty and the admin who withdraws it cannot overwrite each other; repeating any step changes nothing.
/// </summary>
public sealed class BuyerRefusalRevocationTests : IDisposable
{
    private const string Reason = "The buyer was not at fault, the dispute was upheld.";

    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Mock<IUserClient> _users = new();
    private readonly Guid _admin = Guid.NewGuid();

    public void Dispose() => _db.Dispose();

    private static ConfiguredDeliveryFailurePolicy Policy() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeliveryFailure:BuyerRefusedReasonCodes"] = "102",
            ["DeliveryFailure:BuyerRefusalScoreDelta"] = "-5",
            ["DeliveryFailure:TableVersion"] = "test-1"
        }).Build());

    private BuyerRefusalPenaltyService Service() => new(_db, _users.Object, Policy(), NullLogger<BuyerRefusalPenaltyService>.Instance);

    private async Task<SubOrder> SeedAsync(BuyerRefusalPenaltyStatus status)
    {
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = "SUB-" + Guid.NewGuid().ToString("N")[..8], OrderId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Cancelled, PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("A", "0901234567", "1 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"), ContactPhone = "0901234567",
            DeliveryFailureCodes = "102,102", ReturnClassification = "BuyerRefused", BuyerRefusalPenaltyStatus = status,
            ItemsAmount = 220000m, ShippingFeeAmount = 30000m, TotalAmount = 250000m, PaymentDeadlineAt = DateTimeOffset.UtcNow, PaidAt = DateTimeOffset.UtcNow,
        };
        var order = new Order { Id = sub.OrderId, BuyerId = sub.BuyerId, Status = OrderStatus.Paid, TotalAmount = 250000m, PaymentDeadlineAt = DateTimeOffset.UtcNow, PaymentAttemptCount = 1 };
        order.SubOrders.Add(sub);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return sub;
    }

    private async Task<SubOrder> ReloadAsync(Guid id) => await _db.SubOrders.AsNoTracking().FirstAsync(item => item.Id == id);

    private void UserAnswers(int restored) => _users
        .Setup(u => u.RevokeBuyerRefusalPenaltyAsync(It.IsAny<PenaltyRevokeRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PenaltyRevokeResponse(Guid.NewGuid(), "Buyer", false, restored > 0 ? "Restored" : "NothingToRestore", restored, 75, 75 + restored, "Standard"));

    [Fact]
    public async Task An_applied_penalty_is_withdrawn_and_the_points_User_gave_back_are_kept()
    {
        var sub = await SeedAsync(BuyerRefusalPenaltyStatus.Applied);
        UserAnswers(restored: 5);

        var result = await Service().RequestRevocationAsync(sub.Id, _admin, Reason, default);

        result.Status.Should().Be(BuyerRefusalPenaltyStatus.Revoked);
        result.Confirmed.Should().BeTrue();
        result.RestoredPoints.Should().Be(5);
        var saved = await ReloadAsync(sub.Id);
        saved.BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Revoked);
        saved.BuyerRefusalPenaltyRevokedBy.Should().Be(_admin);
        saved.BuyerRefusalPenaltyRevokeReason.Should().Be(Reason);
        saved.BuyerRefusalPenaltyRevokeRequestedAt.Should().NotBeNull();
        saved.BuyerRefusalPenaltyRevokedAt.Should().NotBeNull();
        saved.BuyerRefusalPenaltyRestoredPoints.Should().Be(5);
        _users.Verify(u => u.RevokeBuyerRefusalPenaltyAsync(It.Is<PenaltyRevokeRequest>(r =>
            r.UserId == sub.BuyerId && r.ReferenceId == sub.Id.ToString() && r.ApprovedBy == _admin && r.Note == Reason), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_pending_penalty_is_withdrawn_without_taking_any_points_and_is_never_sent_afterwards()
    {
        var sub = await SeedAsync(BuyerRefusalPenaltyStatus.Pending);
        UserAnswers(restored: 0);
        var service = Service();

        var result = await service.RequestRevocationAsync(sub.Id, _admin, Reason, default);
        var sent = await service.RetryPendingAsync(50, default);

        result.Status.Should().Be(BuyerRefusalPenaltyStatus.Cancelled);
        result.RestoredPoints.Should().Be(0);
        sent.Should().Be(0);
        _users.Verify(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task When_User_is_down_the_withdrawal_stays_RevokePending_and_the_worker_finishes_it_once()
    {
        var sub = await SeedAsync(BuyerRefusalPenaltyStatus.Applied);
        var service = Service();
        _users.SetupSequence(u => u.RevokeBuyerRefusalPenaltyAsync(It.IsAny<PenaltyRevokeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PenaltyRevokeResponse?)null)
            .ReturnsAsync((PenaltyRevokeResponse?)null)
            .ReturnsAsync(new PenaltyRevokeResponse(Guid.NewGuid(), "Buyer", false, "Restored", 5, 75, 80, "Standard"));

        var request = await service.RequestRevocationAsync(sub.Id, _admin, Reason, default);
        request.Confirmed.Should().BeFalse();
        request.Status.Should().Be(BuyerRefusalPenaltyStatus.RevokePending);
        (await service.RetryRevocationsAsync(50, default)).Should().Be(0);
        (await ReloadAsync(sub.Id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.RevokePending);

        (await service.RetryRevocationsAsync(50, default)).Should().Be(1);
        (await service.RetryRevocationsAsync(50, default)).Should().Be(0);      // finished rows are not picked up again

        var saved = await ReloadAsync(sub.Id);
        saved.BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Revoked);
        saved.BuyerRefusalPenaltyRestoredPoints.Should().Be(5);
    }

    [Fact]
    public async Task A_second_approval_changes_nothing_and_does_not_call_User_again()
    {
        var sub = await SeedAsync(BuyerRefusalPenaltyStatus.Applied);
        UserAnswers(restored: 5);
        var service = Service();
        var otherAdmin = Guid.NewGuid();

        await service.RequestRevocationAsync(sub.Id, _admin, Reason, default);
        var second = await service.RequestRevocationAsync(sub.Id, otherAdmin, "A different admin reaches the same decision.", default);

        second.AlreadyRequested.Should().BeTrue();
        second.Status.Should().Be(BuyerRefusalPenaltyStatus.Revoked);
        second.RestoredPoints.Should().Be(5);
        var saved = await ReloadAsync(sub.Id);
        saved.BuyerRefusalPenaltyRevokedBy.Should().Be(_admin);              // the first decision stands
        saved.BuyerRefusalPenaltyRevokeReason.Should().Be(Reason);
        _users.Verify(u => u.RevokeBuyerRefusalPenaltyAsync(It.IsAny<PenaltyRevokeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_admin_who_withdraws_while_the_worker_is_sending_does_not_lose_the_points_the_worker_just_took()
    {
        var sub = await SeedAsync(BuyerRefusalPenaltyStatus.Pending);
        var service = Service();

        // The worker is inside the call to User when the admin withdraws: the withdrawal runs in the middle of the penalty call.
        PenaltyRevocationResult? duringSend = null;
        _users.Setup(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                UserAnswers(restored: 5);
                duringSend = await service.RequestRevocationAsync(sub.Id, _admin, Reason, default);
                return new ReputationAdjustResponse(sub.BuyerId, "Buyer", false, false, 80, 75, -5, "Standard");
            });

        var confirmed = await service.RetryPendingAsync(50, default);

        confirmed.Should().Be(0);                                              // the penalty is not recorded as Applied over the withdrawal
        duringSend!.Status.Should().Be(BuyerRefusalPenaltyStatus.Revoked);
        var saved = await ReloadAsync(sub.Id);
        saved.BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Revoked);
        saved.BuyerRefusalPenaltyRestoredPoints.Should().Be(5);
        _users.Verify(u => u.RevokeBuyerRefusalPenaltyAsync(It.IsAny<PenaltyRevokeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_withdrawal_that_User_could_not_confirm_while_the_worker_applied_the_penalty_is_still_finished_later()
    {
        var sub = await SeedAsync(BuyerRefusalPenaltyStatus.Pending);
        var service = Service();
        _users.SetupSequence(u => u.RevokeBuyerRefusalPenaltyAsync(It.IsAny<PenaltyRevokeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PenaltyRevokeResponse?)null)
            .ReturnsAsync(new PenaltyRevokeResponse(Guid.NewGuid(), "Buyer", false, "Restored", 5, 75, 80, "Standard"));
        _users.Setup(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await service.RequestRevocationAsync(sub.Id, _admin, Reason, default);       // User is unreachable for the withdrawal
                return new ReputationAdjustResponse(sub.BuyerId, "Buyer", false, false, 80, 75, -5, "Standard");
            });

        await service.RetryPendingAsync(50, default);
        (await ReloadAsync(sub.Id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.RevokePending);

        (await service.RetryRevocationsAsync(50, default)).Should().Be(1);

        (await ReloadAsync(sub.Id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Revoked);
    }

    [Fact]
    public async Task A_sub_order_with_no_penalty_cannot_be_withdrawn()
    {
        var sub = await SeedAsync(BuyerRefusalPenaltyStatus.None);

        var act = () => Service().RequestRevocationAsync(sub.Id, _admin, Reason, default);

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("BUYER_REFUSAL_PENALTY_NOT_OWED");
        _users.Verify(u => u.RevokeBuyerRefusalPenaltyAsync(It.IsAny<PenaltyRevokeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too short")]
    [InlineData("         ")]
    public async Task A_withdrawal_needs_a_reason_of_at_least_ten_characters(string reason)
    {
        var sub = await SeedAsync(BuyerRefusalPenaltyStatus.Applied);

        var act = () => Service().RequestRevocationAsync(sub.Id, _admin, reason, default);

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("VAL_INVALID_INPUT");
        (await ReloadAsync(sub.Id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Applied);
    }

    [Fact]
    public async Task An_unknown_sub_order_is_404_and_a_missing_admin_is_403()
    {
        var missing = () => Service().RequestRevocationAsync(Guid.NewGuid(), _admin, Reason, default);
        (await missing.Should().ThrowAsync<CommerceDomainException>()).Which.StatusCode.Should().Be(404);

        var sub = await SeedAsync(BuyerRefusalPenaltyStatus.Applied);
        var noAdmin = () => Service().RequestRevocationAsync(sub.Id, Guid.Empty, Reason, default);
        (await noAdmin.Should().ThrowAsync<CommerceDomainException>()).Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task The_worker_finishes_withdrawals_even_when_the_penalty_rules_are_switched_off()
    {
        var sub = await SeedAsync(BuyerRefusalPenaltyStatus.RevokePending);
        var tracked = await _db.SubOrders.FirstAsync(item => item.Id == sub.Id);
        tracked.BuyerRefusalPenaltyRevokedBy = _admin;
        tracked.BuyerRefusalPenaltyRevokeReason = Reason;
        tracked.BuyerRefusalPenaltyRevokeRequestedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        UserAnswers(restored: 3);
        var switchedOff = new ConfiguredDeliveryFailurePolicy(new ConfigurationBuilder().Build());

        var count = await new BuyerRefusalPenaltyService(_db, _users.Object, switchedOff, NullLogger<BuyerRefusalPenaltyService>.Instance)
            .RetryRevocationsAsync(50, default);

        count.Should().Be(1);
        (await ReloadAsync(sub.Id)).BuyerRefusalPenaltyRestoredPoints.Should().Be(3);
    }
}
