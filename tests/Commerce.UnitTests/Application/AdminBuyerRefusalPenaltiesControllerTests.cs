namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Controllers.Admin;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Delivery;
using Commerce.Domain.Enums;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>Only an admin may withdraw a penalty. The role filter of the project lets staff through every admin route, so the controller checks it itself.</summary>
public sealed class AdminBuyerRefusalPenaltiesControllerTests : IDisposable
{
    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Mock<IUserClient> _users = new();

    public void Dispose() => _db.Dispose();

    private AdminBuyerRefusalPenaltiesController Controller(string role)
    {
        var policy = new ConfiguredDeliveryFailurePolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeliveryFailure:BuyerRefusedReasonCodes"] = "102",
            ["DeliveryFailure:BuyerRefusalScoreDelta"] = "-5"
        }).Build());
        var service = new BuyerRefusalPenaltyService(_db, _users.Object, policy, NullLogger<BuyerRefusalPenaltyService>.Instance);
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role) }, "test");
        return new AdminBuyerRefusalPenaltiesController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } }
        };
    }

    private async Task<Guid> SeedAppliedAsync()
    {
        var id = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sub = new Commerce.Domain.Entities.SubOrder
        {
            Id = id, SubOrderNo = "SUB-" + id.ToString("N")[..8], OrderId = orderId, BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Cancelled, PaymentMethod = PaymentMethod.Wallet, BuyerRefusalPenaltyStatus = BuyerRefusalPenaltyStatus.Applied,
            ShippingAddress = new Commerce.Domain.ValueObjects.ShippingAddressSnapshot("A", "0901234567", "1 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"), ContactPhone = "0901234567",
            ItemsAmount = 1m, ShippingFeeAmount = 1m, TotalAmount = 2m, PaymentDeadlineAt = DateTimeOffset.UtcNow
        };
        var order = new Commerce.Domain.Entities.Order { Id = orderId, BuyerId = sub.BuyerId, Status = OrderStatus.Paid, TotalAmount = 2m, PaymentDeadlineAt = DateTimeOffset.UtcNow, PaymentAttemptCount = 1 };
        order.SubOrders.Add(sub);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return id;
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("buyer")]
    [InlineData("seller")]
    public async Task Anyone_but_an_admin_is_forbidden_and_nothing_changes(string role)
    {
        var id = await SeedAppliedAsync();

        var result = await Controller(role).Revoke(id, new RevokeBuyerRefusalPenaltyRequest { Reason = "The buyer was not at fault, the dispute was upheld." }, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        (await _db.SubOrders.AsNoTracking().FirstAsync(item => item.Id == id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Applied);
        _users.Verify(u => u.RevokeBuyerRefusalPenaltyAsync(It.IsAny<PenaltyRevokeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_admin_withdraws_it()
    {
        var id = await SeedAppliedAsync();
        _users.Setup(u => u.RevokeBuyerRefusalPenaltyAsync(It.IsAny<PenaltyRevokeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PenaltyRevokeResponse(Guid.NewGuid(), "Buyer", false, "Restored", 5, 75, 80, "Standard"));

        var result = await Controller("admin").Revoke(id, new RevokeBuyerRefusalPenaltyRequest { Reason = "The buyer was not at fault, the dispute was upheld." }, default);

        result.Should().BeOfType<OkObjectResult>();
        (await _db.SubOrders.AsNoTracking().FirstAsync(item => item.Id == id)).BuyerRefusalPenaltyStatus.Should().Be(BuyerRefusalPenaltyStatus.Revoked);
    }

    [Fact]
    public async Task While_User_cannot_confirm_the_answer_is_202()
    {
        var id = await SeedAppliedAsync();
        _users.Setup(u => u.RevokeBuyerRefusalPenaltyAsync(It.IsAny<PenaltyRevokeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((PenaltyRevokeResponse?)null);

        var result = await Controller("admin").Revoke(id, new RevokeBuyerRefusalPenaltyRequest { Reason = "The buyer was not at fault, the dispute was upheld." }, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(202);
    }
}
