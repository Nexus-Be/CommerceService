namespace Commerce.UnitTests.Infrastructure;

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;
using Commerce.Infrastructure.Services.User;

public class UserClientTests
{
    private static (UserClient Client, Mock<HttpMessageHandler> Handler) CreateClientWithMockHandler(HttpResponseMessage response)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:8001/")
        };

        var client = new UserClient(httpClient, NullLogger<UserClient>.Instance);
        return (client, mockHandler);
    }

    [Fact]
    public async Task CheckBuyerPermissionAsync_ShouldReturnAllowed_WhenResponseIs200()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var (client, _) = CreateClientWithMockHandler(response);
        var buyerId = Guid.NewGuid();

        // Act
        var result = await client.CheckBuyerPermissionAsync(buyerId);

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task CheckBuyerPermissionAsync_ShouldReturnDisallowed_WhenResponseIs403()
    {
        // Arrange
        var errorContent = JsonSerializer.Serialize(new
        {
            errorCode = "BUYER_ACTION_RESTRICTED",
            message = "User has an active restriction prohibiting purchase actions."
        });
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(errorContent, System.Text.Encoding.UTF8, "application/json")
        };
        var (client, _) = CreateClientWithMockHandler(response);
        var buyerId = Guid.NewGuid();

        // Act
        var result = await client.CheckBuyerPermissionAsync(buyerId);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be("BUYER_ACTION_RESTRICTED");
        result.Message.Should().Be("User has an active restriction prohibiting purchase actions.");
    }

    [Fact]
    public async Task CheckBuyerPermissionAsync_ShouldThrow503_WhenUserServiceFails()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var (client, _) = CreateClientWithMockHandler(response);
        var buyerId = Guid.NewGuid();

        // Act & Assert
        var act = async () => await client.CheckBuyerPermissionAsync(buyerId);
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "USER_SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task AdjustReputationAsync_ShouldReturnResponse_WhenResponseIs200()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var responseBody = JsonSerializer.Serialize(new
        {
            userId,
            targetRole = "Seller",
            isDuplicate = false,
            cooldownSuppressed = false,
            previousScore = 65,
            newScore = 66,
            appliedDelta = 1,
            newTrustLevel = "Standard"
        });

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        };
        var (client, _) = CreateClientWithMockHandler(response);

        var request = new ReputationAdjustRequest(
            userId,
            "Seller",
            Guid.NewGuid(),
            1,
            "OrderCompleted",
            "Order",
            "ORD-20261110-001",
            "Reward for order completion");

        // Act
        var result = await client.AdjustReputationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(userId);
        result.AppliedDelta.Should().Be(1);
        result.NewScore.Should().Be(66);
    }

    [Fact]
    public async Task CreatePenaltyAsync_ShouldReturnResponse_WhenResponseIs200()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var penaltyId = Guid.NewGuid();
        var responseBody = JsonSerializer.Serialize(new
        {
            userId,
            targetRole = "Seller",
            isDuplicate = false,
            penaltyId,
            previousScore = 66,
            newScore = 61,
            appliedDelta = -5,
            newTrustLevel = "Standard"
        });

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        };
        var (client, _) = CreateClientWithMockHandler(response);

        var request = new UserPenaltyRequest(
            userId,
            "Seller",
            "CancellationWithoutReason",
            "Order",
            "ORD-20261110-001",
            "Seller cancelled without reason",
            5,
            "POLICY-SEC-3.2");

        // Act
        var result = await client.CreatePenaltyAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.PenaltyId.Should().Be(penaltyId);
        result.AppliedDelta.Should().Be(-5);
        result.NewScore.Should().Be(61);
    }
}
