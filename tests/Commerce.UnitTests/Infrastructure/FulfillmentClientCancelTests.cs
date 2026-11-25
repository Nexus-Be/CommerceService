namespace Commerce.UnitTests.Infrastructure;

using System;
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
using Commerce.Infrastructure.Services.Fulfillment;

/// <summary>Cancelling the carrier shipment must use the service-to-service route that finds the shipment by sub-order id.</summary>
public class FulfillmentClientCancelTests
{
    [Fact]
    public async Task Cancel_calls_the_internal_route_with_the_sub_order_id()
    {
        HttpRequestMessage? seen = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var client = new FulfillmentClient(new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:8006/") }, NullLogger<FulfillmentClient>.Instance, new ConfigurationBuilder().Build());
        var sub = Guid.NewGuid();

        (await client.CancelShipmentAsync(sub, "BuyerChangedMind")).Should().BeTrue();

        seen!.Method.Should().Be(HttpMethod.Post);
        seen.RequestUri!.AbsolutePath.Should().Be($"/api/v1/internal/shipments/order/{sub}/cancel");
    }

    [Fact]
    public async Task A_refused_cancel_is_reported_as_not_cancelled_and_a_missing_shipment_counts_as_nothing_to_cancel()
    {
        FulfillmentClient With(HttpStatusCode s)
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() => new HttpResponseMessage(s) { Content = new StringContent("{}") });
            return new FulfillmentClient(new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:8006/") }, NullLogger<FulfillmentClient>.Instance, new ConfigurationBuilder().Build());
        }
        (await With(HttpStatusCode.Forbidden).CancelShipmentAsync(Guid.NewGuid())).Should().BeFalse();
        (await With(HttpStatusCode.NotFound).CancelShipmentAsync(Guid.NewGuid())).Should().BeTrue();
    }
}
