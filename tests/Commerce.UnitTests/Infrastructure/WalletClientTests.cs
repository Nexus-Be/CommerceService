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
using Commerce.Infrastructure.Services.Wallet;

public class WalletClientTests
{
    private static (WalletClient Client, Mock<HttpMessageHandler> Handler) CreateClientWithMockHandler(HttpResponseMessage response)
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
            BaseAddress = new Uri("http://localhost:8008/")
        };

        var client = new WalletClient(httpClient, NullLogger<WalletClient>.Instance);
        return (client, mockHandler);
    }

    [Fact]
    public async Task SettleSellerSubOrderAsync_ShouldReturnFalse_WhenConflict409SettlementConflict()
    {
        // Arrange
        var subOrderId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                errorCode = "SETTLEMENT_CONFLICT",
                detail = "An existing settlement conflicts with supplied parameters."
            }))
        };
        var (client, _) = CreateClientWithMockHandler(response);

        // Act
        var result = await client.SettleSellerSubOrderAsync(subOrderId, sellerId, 50000m);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SettleSellerSubOrderAsync_ShouldReturnFalse_WhenConflict409OrderEscrowInsufficient()
    {
        // Arrange
        var subOrderId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                errorCode = "ORDER_ESCROW_INSUFFICIENT",
                detail = "Order escrow has insufficient funds to satisfy payout."
            }))
        };
        var (client, _) = CreateClientWithMockHandler(response);

        // Act
        var result = await client.SettleSellerSubOrderAsync(subOrderId, sellerId, 50000m);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SettleSellerSubOrderAsync_ShouldReturnFalse_WhenFeatureDisabled503()
    {
        // Arrange
        var subOrderId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                errorCode = "FEATURE_DISABLED",
                detail = "ReleaseFeatures:LegacyCommerceCreditsEnabled is disabled."
            }))
        };
        var (client, _) = CreateClientWithMockHandler(response);

        // Act
        var result = await client.SettleSellerSubOrderAsync(subOrderId, sellerId, 50000m);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RefundOrderPaymentAsync_ShouldReturnTrue_WhenSuccess200()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid().ToString();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                id = Guid.NewGuid().ToString(),
                walletId = Guid.NewGuid().ToString(),
                userId = buyerId,
                type = "RefundCredit",
                amount = 100000m,
                currency = "Vnd",
                balanceAfter = 200000m,
                status = "Success",
                note = "Refund for order via wallet",
                createdAt = DateTimeOffset.UtcNow
            }))
        };
        var (client, _) = CreateClientWithMockHandler(response);

        // Act
        var result = await client.RefundOrderPaymentAsync(orderId, buyerId, 100000m, "Buyer cancelled");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RefundOrderPaymentAsync_ShouldReturnFalse_WhenFeatureDisabled503()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid().ToString();
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                errorCode = "FEATURE_DISABLED",
                detail = "ReleaseFeatures:LegacyCommerceCreditsEnabled is disabled."
            }))
        };
        var (client, _) = CreateClientWithMockHandler(response);

        // Act
        var result = await client.RefundOrderPaymentAsync(orderId, buyerId, 100000m, "Buyer cancelled");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task PayOrderAsync_ShouldReturnFailure_WhenInsufficientBalance400()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                errorCode = "INSUFFICIENT_BALANCE",
                detail = "Buyer wallet balance is insufficient to complete the payment."
            }))
        };
        var (client, _) = CreateClientWithMockHandler(response);

        // Act
        var result = await client.PayOrderAsync(orderId, buyerId, 150000m);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("INSUFFICIENT_BALANCE");
        result.IsTransientFailure.Should().BeFalse();
    }
}
