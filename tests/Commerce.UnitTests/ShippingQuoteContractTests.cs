namespace Commerce.UnitTests;

using System.Net;
using System.Text;
using System.Text.Json;
using Commerce.Application.Interfaces;
using Commerce.Domain.Exceptions;
using Commerce.Infrastructure.Services.Fulfillment;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>Commerce must speak Fulfillment's shipping quote contract, or no order (regular or auction) can get a shipping fee.</summary>
public sealed class ShippingQuoteContractTests
{
    private static readonly Guid SellerId = Guid.NewGuid();
    private static readonly Guid SkuId = Guid.NewGuid();

    [Fact]
    public async Task Quote_SendsTheFieldNamesFulfillmentExpects()
    {
        var handler = new CapturingHandler((HttpStatusCode.OK, """{"fee":32000,"isEstimatedFallback":false,"serviceName":"GHN"}"""));
        var client = CreateClient(handler);

        var fee = await client.CalculateShopShippingFeeAsync(new ShopShippingQuoteRequest(
            SellerId, GhnDistrictId: 1450, GhnWardCode: "20804",
            Items: new List<ShippingQuoteItem> { new(SkuId, Quantity: 2, WeightGrams: 750) }));

        Assert.Equal(32000m, fee);
        Assert.Equal("/api/v1/internal/shipping/quote", handler.Path);
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal(SellerId, body.GetProperty("sellerId").GetGuid());
        Assert.Equal(0, body.GetProperty("fromDistrictId").GetInt32());           // 0: Fulfillment uses the seller's warehouse
        Assert.Equal(1450, body.GetProperty("toDistrictId").GetInt32());
        Assert.Equal("20804", body.GetProperty("toWardCode").GetString());
        Assert.Equal(1500, body.GetProperty("weightInGrams").GetInt32());         // 2 x 750 g
        var item = body.GetProperty("items")[0];
        Assert.Equal(SkuId, item.GetProperty("skuId").GetGuid());
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal(750, item.GetProperty("weightInGrams").GetInt32());
        Assert.False(body.TryGetProperty("ghnDistrictId", out _));
    }

    [Fact]
    public async Task Quote_StillReadsTheOlderFeeAmountField()
    {
        var client = CreateClient(new CapturingHandler((HttpStatusCode.OK, """{"feeAmount":41000}""")));

        var fee = await client.CalculateShopShippingFeeAsync(new ShopShippingQuoteRequest(
            SellerId, 1450, "20804", new List<ShippingQuoteItem> { new(SkuId, 1, 500) }));

        Assert.Equal(41000m, fee);
    }

    [Fact]
    public async Task Quote_WhenFulfillmentRejects_ReportsShippingQuoteFailed()
    {
        var client = CreateClient(new CapturingHandler((HttpStatusCode.BadRequest, """{"errorCode":"VAL_INVALID_INPUT"}""")));

        var ex = await Assert.ThrowsAsync<CommerceDomainException>(() => client.CalculateShopShippingFeeAsync(
            new ShopShippingQuoteRequest(SellerId, 1450, "20804", new List<ShippingQuoteItem> { new(SkuId, 1, 500) })));

        Assert.Equal("SHIPPING_QUOTE_FAILED", ex.ErrorCode);
    }

    private static FulfillmentClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://fulfillment.test/") }, NullLogger<FulfillmentClient>.Instance);

    private sealed class CapturingHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _next;
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var (status, body) = responses[Math.Min(_next++, responses.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
