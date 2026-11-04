namespace Commerce.UnitTests.Infrastructure;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;
using Commerce.Infrastructure;
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
            BaseAddress = new Uri("http://localhost:8006/")
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
                req.RequestUri!.ToString().Contains("api/v1/internal/inventories/reservations/batch-release")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task BatchReleaseStockAsync_ShouldThrow503_WhenFulfillmentReturns500()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var orderIds = new List<Guid> { orderId };

        var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var (client, _) = CreateClientWithMockHandler(httpResponse);

        // Act & Assert (Fail-closed: throws CommerceDomainException with 503)
        var act = () => client.BatchReleaseStockAsync(orderIds, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("STOCK_RELEASE_UNAVAILABLE");
        ex.Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task BatchCheckStockAsync_ShouldSendRawArray_ToBySkusEndpoint()
    {
        // Arrange
        var sku1 = Guid.NewGuid();
        var sku2 = Guid.NewGuid();
        var skuIds = new List<Guid> { sku1, sku2 };

        var responsePayload = new[]
        {
            new { skuId = sku1, availableQuantity = 10, safetyStock = 2, reservedQuantity = 0, status = "Active" },
            new { skuId = sku2, availableQuantity = 5, safetyStock = 0, reservedQuantity = 0, status = "Active" }
        };

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responsePayload), System.Text.Encoding.UTF8, "application/json")
        };

        HttpRequestMessage? capturedRequest = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8006/") };
        var client = new FulfillmentClient(httpClient, NullLogger<FulfillmentClient>.Instance);

        // Act
        var result = await client.BatchCheckStockAsync(skuIds, CancellationToken.None);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Contain("api/v1/inventories/by-skus");
        var requestBody = await capturedRequest.Content!.ReadAsStringAsync();
        requestBody.Should().StartWith("[");
        requestBody.Should().Contain(sku1.ToString());
        requestBody.Should().Contain(sku2.ToString());

        result.Should().ContainKey(sku1);
        result[sku1].AvailableStock.Should().Be(10);
        result[sku1].IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ReserveStockAsync_ShouldSendCorrectPayload_ToInternalReservationsEndpoint()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var items = new List<StockReservationItem> { new(skuId, 3, sellerId) };

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK);
        HttpRequestMessage? capturedRequest = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8006/") };
        var client = new FulfillmentClient(httpClient, NullLogger<FulfillmentClient>.Instance);

        // Act
        var success = await client.ReserveStockAsync(orderId, items, 15, CancellationToken.None);

        // Assert
        success.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Contain("api/v1/internal/inventories/reservations");
        var body = await capturedRequest.Content!.ReadAsStringAsync();
        body.Should().Contain(orderId.ToString());
        body.Should().Contain(skuId.ToString());
        body.Should().Contain("\"holdMinutes\":15");
    }

    [Fact]
    public async Task CommitStockAsync_ShouldCallCommitEndpoint_WithPathParameter()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK);
        HttpRequestMessage? capturedRequest = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8006/") };
        var client = new FulfillmentClient(httpClient, NullLogger<FulfillmentClient>.Instance);

        // Act
        var success = await client.CommitStockAsync(orderId, CancellationToken.None);

        // Assert
        success.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Contain($"api/v1/internal/inventories/reservations/{orderId}/commit");
        capturedRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task ReleaseStockAsync_ShouldCallReleaseEndpoint_WithReasonQueryParam()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK);
        HttpRequestMessage? capturedRequest = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8006/") };
        var client = new FulfillmentClient(httpClient, NullLogger<FulfillmentClient>.Instance);

        // Act
        var success = await client.ReleaseStockAsync(orderId, CancellationToken.None);

        // Assert
        success.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Contain($"api/v1/internal/inventories/reservations/{orderId}/release?reason=OrderCancelled");
        capturedRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task ReleaseSubOrderStockAsync_ShouldSendCorrectPayload_WithMasterOrderId()
    {
        // Arrange
        var masterOrderId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var items = new List<StockReservationItem> { new(skuId, 2, sellerId) };

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK);
        HttpRequestMessage? capturedRequest = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8006/") };
        var client = new FulfillmentClient(httpClient, NullLogger<FulfillmentClient>.Instance);

        // Act
        var success = await client.ReleaseSubOrderStockAsync(masterOrderId, items, CancellationToken.None);

        // Assert
        success.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Contain($"api/v1/internal/inventories/reservations/{masterOrderId}/release-items");
        capturedRequest.Method.Should().Be(HttpMethod.Post);

        var body = await capturedRequest.Content!.ReadAsStringAsync();
        body.Should().Contain(skuId.ToString());
        body.Should().Contain("\"quantity\":2");
        body.Should().Contain("\"reason\":\"SubOrderRejected\"");
    }

    [Fact]
    public async Task CalculateShopShippingFeeAsync_ShouldReturnFee_WhenFulfillmentReturns200()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var request = new ShopShippingQuoteRequest(sellerId, 1442, "20101", new List<ShippingQuoteItem>
        {
            new(Guid.NewGuid(), 1, 250)
        });

        var responsePayload = new { feeAmount = 25000m };
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responsePayload), System.Text.Encoding.UTF8, "application/json")
        };

        HttpRequestMessage? capturedRequest = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8006/") };
        var client = new FulfillmentClient(httpClient, NullLogger<FulfillmentClient>.Instance);

        // Act
        var fee = await client.CalculateShopShippingFeeAsync(request, CancellationToken.None);

        // Assert
        fee.Should().Be(25000m);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Contain("api/v1/internal/shipping/quote");
    }

    [Fact]
    public async Task CalculateShopShippingFeeAsync_ShouldThrow503_WhenFulfillmentFails()
    {
        // Arrange (Fail-Closed: No hardcoded 30k fallback)
        var sellerId = Guid.NewGuid();
        var request = new ShopShippingQuoteRequest(sellerId, 1442, "20101", new List<ShippingQuoteItem>
        {
            new(Guid.NewGuid(), 1, 250)
        });

        var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var (client, _) = CreateClientWithMockHandler(httpResponse);

        // Act & Assert
        var act = () => client.CalculateShopShippingFeeAsync(request, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("FULFILLMENT_SERVICE_UNAVAILABLE");
        ex.Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task CalculateShopShippingFeeAsync_ShouldThrow400_WhenFulfillmentReturns400BadRequest()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var request = new ShopShippingQuoteRequest(sellerId, 999999, "INVALID", new List<ShippingQuoteItem>());

        var httpResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"Invalid destination ward\"}")
        };
        var (client, _) = CreateClientWithMockHandler(httpResponse);

        // Act & Assert
        var act = () => client.CalculateShopShippingFeeAsync(request, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("SHIPPING_QUOTE_FAILED");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateShipmentAsync_ShouldCallShipmentsEndpointAndReturnResult_WhenSuccessful()
    {
        // Arrange
        var subOrderId = Guid.NewGuid();
        var responsePayload = new
        {
            trackingCode = "GHN-REAL-12345",
            carrierCode = "GHN",
            labelPrintUrl = "https://fulfillment.nexus.internal/labels/real-label.pdf"
        };

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(responsePayload), System.Text.Encoding.UTF8, "application/json")
        };

        HttpRequestMessage? capturedRequest = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8006/") };
        var client = new FulfillmentClient(httpClient, NullLogger<FulfillmentClient>.Instance);

        // Act
        var result = await client.CreateShipmentAsync(subOrderId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TrackingCode.Should().Be("GHN-REAL-12345");
        result.CarrierCode.Should().Be("GHN");
        result.LabelPrintUrl.Should().Be("https://fulfillment.nexus.internal/labels/real-label.pdf");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Contain("api/v1/internal/shipments");
    }

    [Fact]
    public async Task CreateShipmentAsync_ShouldThrow503_WhenFulfillmentFails()
    {
        // Arrange (Fail-Closed: No fake GHN tracking code fallback)
        var subOrderId = Guid.NewGuid();
        var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var (client, _) = CreateClientWithMockHandler(httpResponse);

        // Act & Assert
        var act = () => client.CreateShipmentAsync(subOrderId, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("FULFILLMENT_SERVICE_UNAVAILABLE");
        ex.Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public void DependencyInjection_ShouldConfigurePort8006Fallback_WhenBaseUrlNotConfigured()
    {
        // Arrange
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5435;Database=commerce_db;Username=postgres;Password=postgres"
        }).Build();

        // Act
        services.AddInfrastructureServices(config);
        var provider = services.BuildServiceProvider();

        // Assert
        var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var client = clientFactory.CreateClient(nameof(IFulfillmentClient));
        client.BaseAddress.Should().Be(new Uri("http://localhost:8006/"));
    }
}
