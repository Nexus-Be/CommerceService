namespace Commerce.UnitTests.Infrastructure;

using System;
using System.Collections.Generic;
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
using Commerce.Infrastructure.Services.Fulfillment;

public class FulfillmentClientTests
{
    private static (FulfillmentClient Client, Mock<HttpMessageHandler> Handler) CreateClientWithMockHandler(HttpResponseMessage response)
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
            BaseAddress = new Uri("http://localhost:8005/")
        };

        var client = new FulfillmentClient(httpClient, NullLogger<FulfillmentClient>.Instance);
        return (client, mockHandler);
    }

    [Fact]
    public async Task BatchReleaseStockAsync_ShouldReturnEmptyResultImmediately_WhenOrderIdsListIsEmpty()
    {
        // Arrange
        var (client, mockHandler) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var result = await client.BatchReleaseStockAsync(new List<Guid>(), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.SucceededOrderIds.Should().BeEmpty();
        result.FailedOrderIds.Should().BeEmpty();
        result.IsSuccess.Should().BeTrue();

        // Verify no HTTP request was made
        mockHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task BatchReleaseStockAsync_ShouldSendBatchRequestAndReturnSucceededIds()
    {
        // Arrange
        var order1 = Guid.NewGuid();
        var order2 = Guid.NewGuid();
        var orderIds = new List<Guid> { order1, order2 };

        var responsePayload = new
        {
            succeededOrderIds = new[] { order1, order2 },
            failedOrderIds = Array.Empty<Guid>()
        };

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responsePayload), System.Text.Encoding.UTF8, "application/json")
        };

        var (client, mockHandler) = CreateClientWithMockHandler(httpResponse);

        // Act
        var result = await client.BatchReleaseStockAsync(orderIds, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.SucceededOrderIds.Should().HaveCount(2);
        result.SucceededOrderIds.Should().Contain(order1);
        result.SucceededOrderIds.Should().Contain(order2);
        result.FailedOrderIds.Should().BeEmpty();

        // Verify exact route
        mockHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("api/v1/fulfillment/inventory/batch-release")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task BatchReleaseStockAsync_ShouldFallbackSafely_WhenFulfillmentReturns500InFailOpen()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var orderIds = new List<Guid> { orderId };

        var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var (client, _) = CreateClientWithMockHandler(httpResponse);

        // Act
        var result = await client.BatchReleaseStockAsync(orderIds, CancellationToken.None);

        // Assert: In default fail-open, returns success so worker continues
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.SucceededOrderIds.Should().Contain(orderId);
    }
}
