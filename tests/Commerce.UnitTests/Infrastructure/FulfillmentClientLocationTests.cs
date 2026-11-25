namespace Commerce.UnitTests.Infrastructure;

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Infrastructure.Services.Fulfillment;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// The location check of Fulfillment is only "confirmed" when the 200 answer names the province, district and ward. An answer that breaks that
/// contract must not let an order keep the text the user typed.
/// </summary>
public class FulfillmentClientLocationTests
{
    private sealed class Fixed(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static FulfillmentClient Client(HttpStatusCode status, string body) =>
        new(new HttpClient(new Fixed(status, body)) { BaseAddress = new Uri("http://localhost:8006/") }, NullLogger<FulfillmentClient>.Instance);

    [Fact]
    public async Task A_200_with_the_three_names_is_confirmed_with_those_names()
    {
        var result = await Client(HttpStatusCode.OK, "{\"provinceName\":\"Hồ Chí Minh\",\"districtName\":\"Quận 1\",\"wardName\":\"Phường Bến Nghé\"}")
            .ValidateLocationAsync(202, 1442, "20101");

        result.Outcome.Should().Be(LocationCheckOutcome.Confirmed);
        result.ProvinceName.Should().Be("Hồ Chí Minh");
        result.DistrictName.Should().Be("Quận 1");
        result.WardName.Should().Be("Phường Bến Nghé");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"provinceName\":\"Hồ Chí Minh\",\"districtName\":\"Quận 1\"}")]
    [InlineData("{\"provinceName\":\"Hồ Chí Minh\",\"districtName\":\"Quận 1\",\"wardName\":\"  \"}")]
    [InlineData("{\"provinceName\":null,\"districtName\":\"Quận 1\",\"wardName\":\"Phường Bến Nghé\"}")]
    public async Task A_200_that_does_not_name_all_three_levels_is_not_confirmed(string body)
    {
        var result = await Client(HttpStatusCode.OK, body).ValidateLocationAsync(202, 1442, "20101");

        result.Outcome.Should().Be(LocationCheckOutcome.SourceUnavailable);
    }

    [Fact]
    public async Task VAL_INVALID_LOCATION_is_rejected_and_other_errors_are_unavailable()
    {
        (await Client(HttpStatusCode.BadRequest, "{\"errorCode\":\"VAL_INVALID_LOCATION\"}").ValidateLocationAsync(202, 1442, "99999"))
            .Outcome.Should().Be(LocationCheckOutcome.Rejected);
        (await Client(HttpStatusCode.ServiceUnavailable, "{\"errorCode\":\"LOCATION_SOURCE_UNAVAILABLE\"}").ValidateLocationAsync(202, 1442, "20101"))
            .Outcome.Should().Be(LocationCheckOutcome.SourceUnavailable);
    }
}
