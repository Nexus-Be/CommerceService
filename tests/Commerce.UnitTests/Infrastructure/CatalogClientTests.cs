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
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;
using Commerce.Infrastructure.Services.Catalog;

public class CatalogClientTests
{
    private static (CatalogClient Client, Mock<HttpMessageHandler> Handler) CreateClientWithMockHandler(HttpResponseMessage response)
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
            BaseAddress = new Uri("http://localhost:8002/")
        };

        var client = new CatalogClient(httpClient, NullLogger<CatalogClient>.Instance);
        return (client, mockHandler);
    }

    [Fact]
    public async Task GetSkuBatchDetailsAsync_ShouldParseItemsAndCalculateIsPurchasable()
    {
        // Arrange
        var sku1 = Guid.NewGuid();
        var sku2 = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var json = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    variantId = sku1,
                    productId = productId,
                    sellerId = sellerId,
                    productName = "Laptop Dell",
                    variantName = "Core i5",
                    price = (decimal?)15000000m,
                    isPurchasable = true,
                    status = "Active",
                    productStatus = "Active"
                },
                new
                {
                    variantId = sku2,
                    productId = productId,
                    sellerId = sellerId,
                    productName = "Laptop Dell Inactive",
                    variantName = "Core i7",
                    price = (decimal?)null,
                    isPurchasable = false,
                    status = "Inactive",
                    productStatus = "Active"
                }
            },
            notFoundIds = Array.Empty<Guid>()
        });

        var (client, _) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

        // Act
        var result = await client.GetSkuBatchDetailsAsync(new[] { sku1, sku2 });

        // Assert
        result.Should().HaveCount(2);
        result[sku1].IsPurchasable.Should().BeTrue();
        result[sku1].UnitPrice.Should().Be(15000000m);
        result[sku1].SellerId.Should().Be(sellerId);

        result[sku2].IsPurchasable.Should().BeFalse();
        result[sku2].UnitPrice.Should().Be(0m);
    }

    [Fact]
    public async Task GetSkuBatchDetailsAsync_ShouldThrow503_WhenCatalogReturns500()
    {
        // Arrange
        var (client, _) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        // Act
        var act = () => client.GetSkuBatchDetailsAsync(new[] { Guid.NewGuid() });

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Where(e => e.ErrorCode == "CATALOG_SERVICE_UNAVAILABLE" && e.StatusCode == 503);
    }

    [Fact]
    public async Task GetSkuBatchDetailsAsync_ShouldThrow503_WhenNetworkFails()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8002/") };
        var client = new CatalogClient(httpClient, NullLogger<CatalogClient>.Instance);

        // Act
        var act = () => client.GetSkuBatchDetailsAsync(new[] { Guid.NewGuid() });

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Where(e => e.ErrorCode == "CATALOG_SERVICE_UNAVAILABLE" && e.StatusCode == 503);
    }

    [Fact]
    public async Task ValidateVariantSwitchAsync_ShouldReturnSuccess_WhenCatalogReturns200()
    {
        // Arrange
        var currentSku = Guid.NewGuid();
        var newSku = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var json = JsonSerializer.Serialize(new
        {
            isValid = true,
            productId = productId,
            sellerId = sellerId,
            newSkuId = newSku,
            newSkuName = "Áo Polo Trắng L",
            newUnitPrice = 120000m,
            isPurchasable = true,
            weightGrams = 250
        });

        var (client, _) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

        // Act
        var result = await client.ValidateVariantSwitchAsync(currentSku, newSku);

        // Assert
        result.IsValid.Should().BeTrue();
        result.NewSkuId.Should().Be(newSku);
        result.NewUnitPrice.Should().Be(120000m);
        result.WeightGrams.Should().Be(250);
    }

    [Fact]
    public async Task ValidateVariantSwitchAsync_ShouldThrowMismatch_WhenCatalogReturns400()
    {
        // Arrange
        var currentSku = Guid.NewGuid();
        var newSku = Guid.NewGuid();

        var json = JsonSerializer.Serialize(new
        {
            status = 400,
            errorCode = "CATALOG_VARIANT_MISMATCH",
            detail = "New SKU does not belong to the same parent product."
        });

        var (client, _) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

        // Act
        var act = () => client.ValidateVariantSwitchAsync(currentSku, newSku);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Where(e => e.ErrorCode == "CATALOG_VARIANT_MISMATCH" && e.StatusCode == 400);
    }

    [Fact]
    public async Task ValidateVariantSwitchAsync_ShouldThrow503_WhenCatalogReturns500()
    {
        // Arrange
        var (client, _) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        // Act
        var act = () => client.ValidateVariantSwitchAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Where(e => e.ErrorCode == "CATALOG_SERVICE_UNAVAILABLE" && e.StatusCode == 503);
    }

    [Fact]
    public async Task ValidateVariantSwitchAsync_ShouldThrow503_WhenNetworkFails()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection reset"));

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8002/") };
        var client = new CatalogClient(httpClient, NullLogger<CatalogClient>.Instance);

        // Act
        var act = () => client.ValidateVariantSwitchAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Where(e => e.ErrorCode == "CATALOG_SERVICE_UNAVAILABLE" && e.StatusCode == 503);
    }

    [Fact]
    public async Task SyncPurchaseRecordAsync_ShouldReturnTrue_When201Created()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var completedAt = DateTimeOffset.UtcNow;

        var json = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            buyerId,
            orderId,
            productId,
            completedAt,
            createdAt = DateTimeOffset.UtcNow
        });

        var (client, _) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

        var request = new CatalogPurchaseSyncRequest(buyerId, orderId, productId, completedAt);

        // Act
        var result = await client.SyncPurchaseRecordAsync(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SyncPurchaseRecordAsync_ShouldReturnTrue_When200Ok()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var (client, _) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        });

        var request = new CatalogPurchaseSyncRequest(buyerId, orderId, productId);

        // Act
        var result = await client.SyncPurchaseRecordAsync(request);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SyncPurchaseRecordAsync_ShouldReturnFalse_When400BadRequest()
    {
        // Arrange
        var (client, _) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"errorCode\":\"VAL_INVALID_INPUT\"}", System.Text.Encoding.UTF8, "application/json")
        });

        var request = new CatalogPurchaseSyncRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = await client.SyncPurchaseRecordAsync(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SyncPurchaseRecordAsync_ShouldReturnFalse_When404NotFound()
    {
        // Arrange
        var (client, _) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"errorCode\":\"PRODUCT_NOT_FOUND\"}", System.Text.Encoding.UTF8, "application/json")
        });

        var request = new CatalogPurchaseSyncRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = await client.SyncPurchaseRecordAsync(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SyncPurchaseRecordAsync_ShouldReturnFalse_When500InternalError()
    {
        // Arrange
        var (client, _) = CreateClientWithMockHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal server error", System.Text.Encoding.UTF8, "text/plain")
        });

        var request = new CatalogPurchaseSyncRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = await client.SyncPurchaseRecordAsync(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SyncPurchaseRecordAsync_ShouldReturnFalse_WhenNetworkExceptionOccurs()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection timed out"));

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8002/") };
        var client = new CatalogClient(httpClient, NullLogger<CatalogClient>.Instance);

        var request = new CatalogPurchaseSyncRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = await client.SyncPurchaseRecordAsync(request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SyncPurchasesBatchAsync_ShouldDeduplicateProductIds_AndExecuteConcurrently()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var product1 = Guid.NewGuid();
        var product2 = Guid.NewGuid();

        int callCount = 0;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                };
            });

        var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://localhost:8002/") };
        var client = new CatalogClient(httpClient, NullLogger<CatalogClient>.Instance);

        // Act: 3 items, but product1 is duplicated
        var productIds = new[] { product1, product2, product1 };
        var successCount = await client.SyncPurchasesBatchAsync(buyerId, orderId, productIds);

        // Assert: Exactly 2 calls made, returns 2
        successCount.Should().Be(2);
        callCount.Should().Be(2);
    }
}
