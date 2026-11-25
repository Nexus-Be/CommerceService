namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Controllers.Admin;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.RefundFund;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// T30 phase 1 in Commerce: the decision and approvals are stored before the Wallet is asked, one case per sub-order, a second different admin
/// above the threshold, the sub-order is shown as refunded only after the Wallet confirms, and a case the fund cannot pay waits in NeedsFinance.
/// </summary>
public sealed class RefundFundServiceTests : IDisposable
{
    private const string Reason = "The seller sent a different product, the dispute was upheld.";

    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Mock<IWalletClient> _wallet = new();
    private readonly Guid _adminA = Guid.NewGuid();
    private readonly Guid _adminB = Guid.NewGuid();

    public void Dispose() => _db.Dispose();

    private RefundFundService Service(string? threshold = null) => new(_db, _wallet.Object,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RefundFund:SecondApprovalThreshold"] = threshold }).Build(),
        NullLogger<RefundFundService>.Instance);

    private async Task<SubOrder> SeedAsync(decimal total = 100_000m, PaymentMethod method = PaymentMethod.Wallet, bool settled = true, SubOrderStatus status = SubOrderStatus.Completed)
    {
        var orderId = Guid.NewGuid();
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = "SUB-" + Guid.NewGuid().ToString("N")[..8], OrderId = orderId, BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            Status = status, PaymentMethod = method, IsEscrowSettled = settled,
            ShippingAddress = new ShippingAddressSnapshot("A", "0901234567", "1 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"), ContactPhone = "0901234567",
            ItemsAmount = total, ShippingFeeAmount = 0m, TotalAmount = total, PaymentDeadlineAt = DateTimeOffset.UtcNow, PaidAt = DateTimeOffset.UtcNow
        };
        var order = new Order { Id = orderId, BuyerId = sub.BuyerId, Status = OrderStatus.Paid, TotalAmount = total, PaymentDeadlineAt = DateTimeOffset.UtcNow, PaymentAttemptCount = 1 };
        order.SubOrders.Add(sub);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return sub;
    }

    private void WalletAnswers(string outcome, string? reason = null, decimal recovered = 95_000m, decimal shortfall = 0m) => _wallet
        .Setup(w => w.PayRefundFundCaseAsync(It.IsAny<RefundFundPayRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RefundFundPayResponse(outcome, reason, 100_000m, recovered, shortfall, false, Guid.NewGuid().ToString()));

    private async Task<RefundFundCase> CaseAsync(Guid subOrderId) => await _db.RefundFundCases.AsNoTracking().FirstAsync(item => item.SubOrderId == subOrderId);

    private async Task<SubOrderStatus> SubStatusAsync(Guid subOrderId) => (await _db.SubOrders.AsNoTracking().FirstAsync(item => item.Id == subOrderId)).Status;

    [Fact]
    public async Task An_approved_case_is_refunded_and_only_then_is_the_sub_order_shown_as_refunded()
    {
        var sub = await SeedAsync();
        WalletAnswers("Refunded");

        var result = await Service().RequestAsync(sub.Id, _adminA, Reason, Guid.NewGuid(), default);

        result.Status.Should().Be("Refunded");
        result.SellerRecovered.Should().Be(95_000m);
        result.BuyerTransactionId.Should().NotBeNull();
        (await SubStatusAsync(sub.Id)).Should().Be(SubOrderStatus.Refunded);
        _wallet.Verify(w => w.PayRefundFundCaseAsync(It.Is<RefundFundPayRequest>(r =>
            r.SubOrderId == sub.Id && r.ParentOrderId == sub.OrderId && r.BuyerId == sub.BuyerId && r.Amount == 100_000m
            && r.FirstApproverId == _adminA && r.SecondApproverId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Two_reports_about_the_same_sub_order_reach_one_case_and_one_refund()
    {
        var sub = await SeedAsync();
        WalletAnswers("Refunded");
        var service = Service();

        var first = await service.RequestAsync(sub.Id, _adminA, Reason, Guid.NewGuid(), default);
        var second = await service.RequestAsync(sub.Id, _adminB, "A second report about the same sub-order.", Guid.NewGuid(), default);

        second.Id.Should().Be(first.Id);
        second.AlreadyExisted.Should().BeTrue();
        (await _db.RefundFundCases.CountAsync()).Should().Be(1);
        (await CaseAsync(sub.Id)).FirstApproverId.Should().Be(_adminA);        // the first decision stands
        _wallet.Verify(w => w.PayRefundFundCaseAsync(It.IsAny<RefundFundPayRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Above_the_threshold_the_wallet_is_not_asked_until_a_different_admin_approves()
    {
        var sub = await SeedAsync(total: 300_000m);
        WalletAnswers("Refunded");
        var service = Service(threshold: "200000");

        var waiting = await service.RequestAsync(sub.Id, _adminA, Reason, null, default);
        waiting.Status.Should().Be("AwaitingSecondApproval");
        _wallet.Verify(w => w.PayRefundFundCaseAsync(It.IsAny<RefundFundPayRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        var sameAdmin = () => service.ApproveSecondAsync(sub.Id, _adminA, default);
        (await sameAdmin.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("SECOND_APPROVER_MUST_DIFFER");

        var approved = await service.ApproveSecondAsync(sub.Id, _adminB, default);
        approved.Status.Should().Be("Refunded");
        _wallet.Verify(w => w.PayRefundFundCaseAsync(It.Is<RefundFundPayRequest>(r => r.FirstApproverId == _adminA && r.SecondApproverId == _adminB), It.IsAny<CancellationToken>()), Times.Once);

        var again = await service.ApproveSecondAsync(sub.Id, Guid.NewGuid(), default);
        again.Status.Should().Be("Refunded");                                     // a repeat changes nothing and asks nobody
        _wallet.Verify(w => w.PayRefundFundCaseAsync(It.IsAny<RefundFundPayRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task While_the_wallet_cannot_be_reached_the_decision_waits_and_the_worker_finishes_it_once()
    {
        var sub = await SeedAsync();
        var service = Service();
        _wallet.SetupSequence(w => w.PayRefundFundCaseAsync(It.IsAny<RefundFundPayRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefundFundPayResponse?)null)
            .ReturnsAsync(new RefundFundPayResponse("Refunded", null, 100_000m, 95_000m, 0m, false, Guid.NewGuid().ToString()));

        var first = await service.RequestAsync(sub.Id, _adminA, Reason, null, default);
        first.Status.Should().Be("Pending");
        first.Attempts.Should().Be(1);
        (await SubStatusAsync(sub.Id)).Should().Be(SubOrderStatus.Completed);      // not shown as refunded before the wallet confirms

        (await service.RetryPendingAsync(50, default)).Should().Be(1);
        (await service.RetryPendingAsync(50, default)).Should().Be(0);             // a finished case is not picked up again

        (await CaseAsync(sub.Id)).Status.Should().Be(RefundFundCaseStatus.Refunded);
        (await SubStatusAsync(sub.Id)).Should().Be(SubOrderStatus.Refunded);
        _wallet.Verify(w => w.PayRefundFundCaseAsync(It.IsAny<RefundFundPayRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_case_the_fund_cannot_pay_waits_in_needs_finance_and_moves_nothing()
    {
        var sub = await SeedAsync();
        WalletAnswers("NeedsFinance", reason: "FUND_INSUFFICIENT", recovered: 0m);
        var service = Service();

        var result = await service.RequestAsync(sub.Id, _adminA, Reason, null, default);

        result.Status.Should().Be("NeedsFinance");
        result.NeedsFinanceReason.Should().Be("FUND_INSUFFICIENT");
        (await SubStatusAsync(sub.Id)).Should().Be(SubOrderStatus.Completed);
        (await service.ListAsync(RefundFundCaseStatus.NeedsFinance, 1, 20, default)).Items.Should().ContainSingle();
        (await service.RetryPendingAsync(50, default)).Should().Be(0);             // the worker does not touch it

        // after the fund is topped up an admin asks again
        WalletAnswers("Refunded");
        var retried = await service.RetryAsync(sub.Id, _adminB, default);
        retried.Status.Should().Be("Refunded");
        (await SubStatusAsync(sub.Id)).Should().Be(SubOrderStatus.Refunded);
    }

    [Fact]
    public async Task A_refusal_the_wallet_will_not_change_is_kept_for_a_person_and_can_be_closed_with_a_note()
    {
        var sub = await SeedAsync();
        WalletAnswers("Rejected", reason: "SUB_ORDER_NOT_SETTLED");
        var service = Service();

        var result = await service.RequestAsync(sub.Id, _adminA, Reason, null, default);

        result.Status.Should().Be("NeedsFinance");
        result.NeedsFinanceReason.Should().Be("WALLET_SUB_ORDER_NOT_SETTLED");

        var tooShort = () => service.ResolveAsync(sub.Id, _adminB, "done", default);
        (await tooShort.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("VAL_INVALID_INPUT");
        var closed = await service.ResolveAsync(sub.Id, _adminB, "The buyer was refunded by bank transfer outside the system.", default);
        closed.Status.Should().Be("Resolved");
        closed.ResolvedBy.Should().Be(_adminB);
        (await service.ResolveAsync(sub.Id, _adminA, "Closing the same case a second time.", default)).ResolvedBy.Should().Be(_adminB);
    }

    [Fact]
    public async Task Only_a_paid_out_wallet_sub_order_without_another_refund_can_use_the_fund()
    {
        var service = Service();
        WalletAnswers("Refunded");

        var cod = await SeedAsync(method: PaymentMethod.Cod);
        (await service.Invoking(s => s.RequestAsync(cod.Id, _adminA, Reason, null, default)).Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("REFUND_FUND_WALLET_ONLY");

        var notPaidOut = await SeedAsync(settled: false);
        (await service.Invoking(s => s.RequestAsync(notPaidOut.Id, _adminA, Reason, null, default)).Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("SUB_ORDER_NOT_PAID_OUT");

        var alreadyRefunded = await SeedAsync();
        _db.OrderRefunds.Add(new OrderRefund { SubOrderId = alreadyRefunded.Id, Amount = 100_000m, Status = RefundStatus.Completed, ReasonCode = "SELLER_REJECTED", RefundType = RefundType.Full, ActorRole = OrderActorRole.System });
        await _db.SaveChangesAsync();
        (await service.Invoking(s => s.RequestAsync(alreadyRefunded.Id, _adminA, Reason, null, default)).Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("REFUND_ALREADY_EXISTS");

        (await service.Invoking(s => s.RequestAsync(Guid.NewGuid(), _adminA, Reason, null, default)).Should().ThrowAsync<CommerceDomainException>()).Which.StatusCode.Should().Be(404);
        (await service.Invoking(s => s.RequestAsync(cod.Id, _adminA, "short", null, default)).Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("VAL_INVALID_INPUT");
        (await _db.RefundFundCases.CountAsync()).Should().Be(0);
        _wallet.Verify(w => w.PayRefundFundCaseAsync(It.IsAny<RefundFundPayRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private AdminRefundFundCasesController Controller(string role)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role) }, "test");
        return new AdminRefundFundCasesController(Service())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } }
        };
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("buyer")]
    public async Task Only_an_admin_can_decide_staff_can_only_read(string role)
    {
        var sub = await SeedAsync();
        WalletAnswers("Refunded");

        var result = await Controller(role).Request(new RequestRefundFundCaseRequest { SubOrderId = sub.Id, Reason = Reason }, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        (await _db.RefundFundCases.CountAsync()).Should().Be(0);
        (await Controller("staff").List(null, 1, 20, default)).Should().BeOfType<OkObjectResult>();
        (await Controller("admin").Request(new RequestRefundFundCaseRequest { SubOrderId = sub.Id, Reason = Reason }, default)).Should().BeOfType<OkObjectResult>();
    }
}
