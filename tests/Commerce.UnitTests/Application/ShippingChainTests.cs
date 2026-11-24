namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Common;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;
using Commerce.Infrastructure.Services.Fulfillment;
using Commerce.Infrastructure.Services.User;

/// <summary>
/// A delivery address is used only when its province, district and ward are all given and the GHN lists confirm that they belong together.
/// The carrier's fee answer is not proof: it prices by the ward and ignores a district that does not match.
/// </summary>
public class ShippingChainTests
{
    private static Mock<IFulfillmentClient> Fulfillment(LocationCheckResult? answer)
    {
        var mock = new Mock<IFulfillmentClient>();
        mock.Setup(f => f.ValidateLocationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(answer!);
        return mock;
    }

    [Fact]
    public async Task A_confirmed_chain_passes_and_the_ward_is_asked_without_spaces()
    {
        var mock = Fulfillment(LocationCheckResult.Confirmed());

        await ShippingChain.EnsureConfirmedAsync(mock.Object, 202, 1442, " 20101 ");

        mock.Verify(f => f.ValidateLocationAsync(202, 1442, "20101", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null, 1442, "20101")]
    [InlineData(0, 1442, "20101")]
    [InlineData(202, null, "20101")]
    [InlineData(202, 0, "20101")]
    [InlineData(202, 1442, null)]
    [InlineData(202, 1442, "  ")]
    public async Task A_missing_part_is_400_and_GHN_is_not_asked(int? province, int? district, string? ward)
    {
        var mock = Fulfillment(LocationCheckResult.Confirmed());

        var act = () => ShippingChain.EnsureConfirmedAsync(mock.Object, province, district, ward);

        var ex = (await act.Should().ThrowAsync<CommerceDomainException>()).Which;
        ex.ErrorCode.Should().Be("INVALID_SHIPPING_ADDRESS");
        ex.StatusCode.Should().Be(400);
        mock.Verify(f => f.ValidateLocationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_chain_the_lists_reject_is_400()
    {
        var act = () => ShippingChain.EnsureConfirmedAsync(Fulfillment(LocationCheckResult.Rejected()).Object, 202, 1442, "144201");

        var ex = (await act.Should().ThrowAsync<CommerceDomainException>()).Which;
        ex.ErrorCode.Should().Be("INVALID_SHIPPING_ADDRESS");
        ex.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData(LocationCheckOutcome.SourceUnavailable)]
    [InlineData(LocationCheckOutcome.Unknown)]
    public async Task With_no_trusted_GHN_source_it_is_503(LocationCheckOutcome outcome)
    {
        var act = () => ShippingChain.EnsureConfirmedAsync(Fulfillment(new LocationCheckResult(outcome)).Object, 202, 1442, "20101");

        var ex = (await act.Should().ThrowAsync<CommerceDomainException>()).Which;
        ex.ErrorCode.Should().Be("LOCATION_SOURCE_UNAVAILABLE");
        ex.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task No_answer_at_all_is_treated_as_no_trusted_source()
    {
        var act = () => ShippingChain.EnsureConfirmedAsync(Fulfillment(null).Object, 202, 1442, "20101");

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.StatusCode.Should().Be(503);
    }

    // ---- the Fulfillment client: what each answer of Fulfillment means
    private sealed class Handler(HttpStatusCode status, string body, Action<HttpRequestMessage>? seen = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("down");
    }

    private static FulfillmentClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8006/") }, NullLogger<FulfillmentClient>.Instance, new ConfigurationBuilder().Build());

    [Fact]
    public async Task The_client_asks_the_internal_route_and_reads_a_confirmed_answer()
    {
        HttpRequestMessage? seen = null;
        var client = Client(new Handler(HttpStatusCode.OK, "{\"isValid\":true}", r => seen = r));

        var result = await client.ValidateLocationAsync(202, 1442, "20101");

        result.Outcome.Should().Be(LocationCheckOutcome.Confirmed);
        seen!.Method.Should().Be(HttpMethod.Post);
        seen.RequestUri!.AbsolutePath.Should().Be("/api/v1/internal/shipping/locations/validate");
        (await seen.Content!.ReadAsStringAsync()).Should().Contain("202").And.Contain("1442").And.Contain("20101");
    }

    [Fact]
    public async Task The_client_tells_a_rejected_chain_from_a_source_that_is_down()
    {
        (await Client(new Handler(HttpStatusCode.BadRequest, "{\"errorCode\":\"VAL_INVALID_LOCATION\"}")).ValidateLocationAsync(202, 1442, "x"))
            .Outcome.Should().Be(LocationCheckOutcome.Rejected);
        (await Client(new Handler(HttpStatusCode.ServiceUnavailable, "{\"errorCode\":\"LOCATION_SOURCE_UNAVAILABLE\"}")).ValidateLocationAsync(202, 1442, "x"))
            .Outcome.Should().Be(LocationCheckOutcome.SourceUnavailable);
        (await Client(new Handler(HttpStatusCode.BadRequest, "{\"errorCode\":\"SOMETHING_ELSE\"}")).ValidateLocationAsync(202, 1442, "x"))
            .Outcome.Should().Be(LocationCheckOutcome.SourceUnavailable, "a 400 that is not the location rejection says nothing about the chain");
        (await Client(new Throwing()).ValidateLocationAsync(202, 1442, "x")).Outcome.Should().Be(LocationCheckOutcome.SourceUnavailable);
    }

    // ---- the address UserService really sends
    [Fact]
    public async Task The_address_book_answer_UserService_sends_is_read_with_its_own_field_names()
    {
        // This is the shape of InternalUserAddressResponse. Reading only the Ghn-prefixed names left the district and ward at 0.
        var body = "{\"addressId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"recipientName\":\"Nguyen A\"," +
                   "\"phone\":\"0912345678\",\"label\":\"Home\",\"provinceId\":202,\"provinceName\":\"Ho Chi Minh\",\"districtId\":1442,\"districtName\":\"Quan 1\"," +
                   "\"wardCode\":\"20101\",\"wardName\":\"Phuong Ben Nghe\",\"addressLine\":\"1 Le Loi\",\"isDefault\":true}";
        var http = new HttpClient(new Handler(HttpStatusCode.OK, body)) { BaseAddress = new Uri("http://localhost:8001/") };
        var client = new UserClient(http, NullLogger<UserClient>.Instance);

        var address = await client.GetBuyerAddressAsync(Guid.NewGuid(), Guid.Parse("11111111-1111-1111-1111-111111111111"));

        address.Should().NotBeNull();
        address!.Id.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        address.ProvinceId.Should().Be(202);
        address.GhnDistrictId.Should().Be(1442);
        address.GhnWardCode.Should().Be("20101");
        address.Street.Should().Be("1 Le Loi");
        address.Ward.Should().Be("Phuong Ben Nghe");
        address.District.Should().Be("Quan 1");
        address.Province.Should().Be("Ho Chi Minh");
        address.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task The_older_Ghn_prefixed_answer_is_still_read()
    {
        var body = "{\"id\":\"11111111-1111-1111-1111-111111111111\",\"recipientName\":\"A\",\"phone\":\"0912345678\",\"street\":\"1 Le Loi\",\"ward\":\"W\",\"district\":\"D\"," +
                   "\"province\":\"P\",\"ghnDistrictId\":1442,\"ghnWardCode\":\"20101\",\"fullAddressText\":\"x\",\"isDefault\":false}";
        var http = new HttpClient(new Handler(HttpStatusCode.OK, body)) { BaseAddress = new Uri("http://localhost:8001/") };

        var address = await new UserClient(http, NullLogger<UserClient>.Instance).GetBuyerAddressAsync(Guid.NewGuid(), Guid.NewGuid());

        address!.GhnDistrictId.Should().Be(1442);
        address.GhnWardCode.Should().Be("20101");
        address.ProvinceId.Should().BeNull();
    }
}
