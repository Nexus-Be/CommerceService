namespace Commerce.UnitTests.Infrastructure;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;
using Commerce.Infrastructure.Services.Fulfillment;

/// <summary>
/// A seller with no active warehouse gets WAREHOUSE_NOT_FOUND from Fulfillment. Commerce must say so (SELLER_WAREHOUSE_MISSING) instead of
/// reporting the fulfillment service as unreachable, while every other failure keeps its previous meaning.
/// </summary>
public class FulfillmentClientWarehouseTests
{
    private static FulfillmentClient CreateClient(HttpStatusCode status, string body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(status) { Content = new StringContent(body) });
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:8006/") };
        return new FulfillmentClient(http, NullLogger<FulfillmentClient>.Instance, new ConfigurationBuilder().Build());
    }

    private const string MissingWarehouse = "{\"title\":\"Not found\",\"status\":404,\"errorCode\":\"WAREHOUSE_NOT_FOUND\"}";

    private static ShopShippingQuoteRequest Quote() =>
        new(Guid.NewGuid(), 1442, "20101", new List<ShippingQuoteItem>());

    private static ShipmentCreationRequest Create() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Buyer", "0900000001", "12 Nguyen Hue", null, 1442, "20101", 500,
            new List<ShipmentCreationItem> { new(Guid.NewGuid(), 1, "Item", 100000m, 500) });

    [Fact]
    public async Task Quote_WhenTheSellerHasNoWarehouse_SaysTheShopIsNotReady_NotThatFulfillmentIsDown()
    {
        var act = () => CreateClient(HttpStatusCode.NotFound, MissingWarehouse).CalculateShopShippingFeeAsync(Quote());

        var ex = (await act.Should().ThrowAsync<CommerceDomainException>()).Which;
        ex.ErrorCode.Should().Be("SELLER_WAREHOUSE_MISSING");
        ex.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Quote_ANotFoundWithAnotherCode_StillMeansUnavailable()
    {
        var act = () => CreateClient(HttpStatusCode.NotFound, "{\"errorCode\":\"SOMETHING_ELSE\"}").CalculateShopShippingFeeAsync(Quote());

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("FULFILLMENT_SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task Quote_ABodyThatIsNotJson_StillMeansUnavailable()
    {
        var act = () => CreateClient(HttpStatusCode.NotFound, "<html>not found</html>").CalculateShopShippingFeeAsync(Quote());

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("FULFILLMENT_SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task Quote_AServerError_IsStillUnavailable()
    {
        var act = () => CreateClient(HttpStatusCode.InternalServerError, "{}").CalculateShopShippingFeeAsync(Quote());

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("FULFILLMENT_SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task CreateShipment_WhenTheSellerHasNoWarehouse_TellsTheSellerToSetItUpThenRetry()
    {
        var act = () => CreateClient(HttpStatusCode.NotFound, MissingWarehouse).CreateShipmentAsync(Create());

        var ex = (await act.Should().ThrowAsync<CommerceDomainException>()).Which;
        ex.ErrorCode.Should().Be("SELLER_WAREHOUSE_MISSING");
        ex.Message.Should().Contain("warehouse");
    }

    [Fact]
    public async Task Quote_WhenTheWarehouseHasNoOriginDistrict_SaysTheWarehouseIsIncomplete_NotThatTheAddressIsInvalid()
    {
        var act = () => CreateClient(HttpStatusCode.BadRequest, "{\"errorCode\":\"INVALID_FROM_DISTRICT\"}").CalculateShopShippingFeeAsync(Quote());

        var ex = (await act.Should().ThrowAsync<CommerceDomainException>()).Which;
        ex.ErrorCode.Should().Be("SELLER_WAREHOUSE_INCOMPLETE");
        ex.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Quote_AnotherBadRequest_StillMeansTheAddressOrItemsAreInvalid()
    {
        var act = () => CreateClient(HttpStatusCode.BadRequest, "{\"errorCode\":\"VAL_INVALID_INPUT\"}").CalculateShopShippingFeeAsync(Quote());

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("SHIPPING_QUOTE_FAILED");
    }

    [Fact]
    public async Task CreateShipment_WhenTheWarehouseHasNoOriginDistrict_TellsTheSellerToCompleteTheAddress()
    {
        var act = () => CreateClient(HttpStatusCode.BadRequest, "{\"errorCode\":\"INVALID_FROM_DISTRICT\"}").CreateShipmentAsync(Create());

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("SELLER_WAREHOUSE_INCOMPLETE");
    }

    [Fact]
    public async Task CreateShipment_AnotherRefusal_KeepsTheGenericRejectionCode()
    {
        var act = () => CreateClient(HttpStatusCode.BadRequest, "{\"errorCode\":\"VAL_INVALID_INPUT\"}").CreateShipmentAsync(Create());

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("SHIPMENT_CREATION_REJECTED");
    }
}
