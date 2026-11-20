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
/// Q6: a stock reservation that fails must never let an order through unless fail-open is switched on explicitly (isolated QA).
/// A 4xx answer is a business refusal and is always a rejection; an outage or 5xx is reported as 503 FULFILLMENT_UNAVAILABLE.
/// </summary>
public class FulfillmentClientStockPolicyTests
{
    private static readonly List<StockReservationItem> Items = new() { new(Guid.NewGuid(), 2, Guid.NewGuid()) };

    private static FulfillmentClient CreateClient(HttpStatusCode? status, bool? failOpen, bool throwOnSend = false)
    {
        var handler = new Mock<HttpMessageHandler>();
        var setup = handler.Protected().Setup<Task<HttpResponseMessage>>(
            "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        if (throwOnSend)
        {
            setup.ThrowsAsync(new HttpRequestException("connection refused"));
        }
        else
        {
            setup.ReturnsAsync(new HttpResponseMessage(status!.Value) { Content = new StringContent("{}") });
        }

        var values = new Dictionary<string, string?>();
        if (failOpen.HasValue)
        {
            values["StockPolicy:FailOpen"] = failOpen.Value.ToString();
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:8006/") };
        return new FulfillmentClient(http, NullLogger<FulfillmentClient>.Instance, configuration);
    }

    [Fact]
    public async Task Reserve_WhenFulfillmentAnswersServerError_AndNothingIsConfigured_FailsClosedWith503()
    {
        var client = CreateClient(HttpStatusCode.InternalServerError, failOpen: null);

        var act = () => client.ReserveStockAsync(Guid.NewGuid(), Items);

        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("FULFILLMENT_UNAVAILABLE");
        ex.Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task Reserve_WhenFulfillmentIsUnreachable_AndNothingIsConfigured_FailsClosedWith503()
    {
        var client = CreateClient(status: null, failOpen: null, throwOnSend: true);

        var act = () => client.ReserveStockAsync(Guid.NewGuid(), Items);

        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("FULFILLMENT_UNAVAILABLE");
        ex.Which.StatusCode.Should().Be(503);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reserve_WhenFulfillmentRefusesWithA4xx_IsAlwaysARejection_EvenWhenFailOpenIsOn(bool? failOpen)
    {
        var client = CreateClient(HttpStatusCode.BadRequest, failOpen);

        var reserved = await client.ReserveStockAsync(Guid.NewGuid(), Items);

        reserved.Should().BeFalse();
    }

    [Fact]
    public async Task Reserve_WhenFailOpenIsSwitchedOnExplicitly_AServerErrorLetsTheOrderThrough()
    {
        var client = CreateClient(HttpStatusCode.InternalServerError, failOpen: true);

        var reserved = await client.ReserveStockAsync(Guid.NewGuid(), Items);

        reserved.Should().BeTrue();
    }

    [Fact]
    public async Task Reserve_WhenFailOpenIsSwitchedOnExplicitly_AnOutageLetsTheOrderThrough()
    {
        var client = CreateClient(status: null, failOpen: true, throwOnSend: true);

        var reserved = await client.ReserveStockAsync(Guid.NewGuid(), Items);

        reserved.Should().BeTrue();
    }

    [Fact]
    public async Task Reserve_WhenFulfillmentAccepts_ReturnsTrue()
    {
        var client = CreateClient(HttpStatusCode.OK, failOpen: null);

        var reserved = await client.ReserveStockAsync(Guid.NewGuid(), Items);

        reserved.Should().BeTrue();
    }
}
