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
                    price = 15000000m,
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
                    price = 20000000m,
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

        result[sku2].IsPurchasable.Should().BeFalse(); // Do status = Inactive
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
}
