namespace Commerce.UnitTests;

using System.Net;
using System.Text;
using System.Text.Json;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Shipment;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;
using Commerce.Infrastructure.Services.Fulfillment;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Fulfillment rejects a shipment request that lacks the order, seller and recipient, so a seller could never accept an order.
/// These tests pin the request built for each flow and the contract the client speaks.
/// </summary>
public sealed class ShipmentCreationContractTests
{
    private const string AuctionAddressJson =
        """{"fullName":"E2E Buyer","phoneNumber":"0912345678","addressLine":"456 Tran Hung Dao","provinceName":"Da Nang","districtName":"Hai Chau","wardName":"Thach Thang","ghnDistrictId":1450,"ghnWardCode":"20804"}""";

    [Fact]
    public void AuctionOrder_BuildsACompleteRequest()
    {
        var order = NewAuctionOrder(AuctionAddressJson, weight: 750);

        var request = ShipmentRequestFactory.ForAuctionOrder(order);

        Assert.Equal(order.Id, request.OrderId);
        Assert.Equal(order.Id, request.SubOrderId);              // an auction order has no parent order
        Assert.Equal(order.SellerId, request.SellerId);
        Assert.Equal("E2E Buyer", request.RecipientName);
        Assert.Equal("0912345678", request.RecipientPhone);
        Assert.Equal("456 Tran Hung Dao, Thach Thang, Hai Chau, Da Nang", request.ShippingAddress);
        Assert.Equal(1450, request.ToDistrictId);
        Assert.Equal("20804", request.ToWardCode);
        Assert.Equal(750, request.WeightInGrams);
        var item = Assert.Single(request.Items);
        Assert.Equal(order.SkuId, item.SkuId);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(750, item.WeightInGrams);
    }

    [Fact]
    public void AuctionOrder_WithoutSnapshotWeight_UsesTheDefault()
    {
        var request = ShipmentRequestFactory.ForAuctionOrder(NewAuctionOrder(AuctionAddressJson, weight: 0));

        Assert.Equal(500, request.WeightInGrams);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void AuctionOrder_WithoutAReadableAddress_IsRefusedBeforeCallingFulfillment(string? address)
    {
        var order = NewAuctionOrder(address, weight: 750);

        var ex = Assert.Throws<CommerceDomainException>(() => ShipmentRequestFactory.ForAuctionOrder(order));

        Assert.Equal("SHIPPING_ADDRESS_MISSING", ex.ErrorCode);
    }

    [Fact]
    public void SubOrder_BuildsACompleteRequestFromItsSnapshot()
    {
        var skuId = Guid.NewGuid();
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = "SUB-1", OrderId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Confirmed, PaymentMethod = PaymentMethod.Wallet, ContactPhone = "0900000004",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(1),
            ShippingAddress = new ShippingAddressSnapshot
            {
                ReceiverName = "John Buyer", ContactPhone = "0900000004", Street = "123 Nguyen Trai", Ward = "Thanh Xuan Trung",
                District = "Thanh Xuan", Province = "Ha Noi", GhnDistrictId = 1442, GhnWardCode = "20101"
            }
        };
        subOrder.Items.Add(new SubOrderItem
        {
            Id = Guid.NewGuid(), SubOrderId = subOrder.Id, ProductId = Guid.NewGuid(), SkuId = skuId, SellerId = subOrder.SellerId,
            ProductNameSnapshot = "Kettle", Quantity = 2, UnitPrice = 850000m, UnitPriceAfterDiscount = 800000m, LineTotal = 1600000m
        });

        var request = ShipmentRequestFactory.ForSubOrder(subOrder);

        Assert.Equal(subOrder.OrderId, request.OrderId);
        Assert.Equal(subOrder.Id, request.SubOrderId);
        Assert.Equal(subOrder.SellerId, request.SellerId);
        Assert.Equal("John Buyer", request.RecipientName);
        Assert.Equal("0900000004", request.RecipientPhone);
        Assert.Equal("123 Nguyen Trai, Thanh Xuan Trung, Thanh Xuan, Ha Noi", request.ShippingAddress);
        Assert.Equal(1442, request.ToDistrictId);
        Assert.Equal("20101", request.ToWardCode);
        var item = Assert.Single(request.Items);
        Assert.Equal(skuId, item.SkuId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(800000m, item.Price);
    }

    [Fact]
    public async Task Client_SendsFulfillmentsFieldNames_AndReadsItsRealAnswer()
    {
        var handler = new CapturingHandler((HttpStatusCode.OK,
            """{"shipmentId":"11111111-1111-1111-1111-111111111111","trackingNumber":"GHN1234567","labelUrl":"https://labels.test/GHN1234567.pdf","status":"ReadyToPick"}"""));
        var client = CreateClient(handler);
        var request = ShipmentRequestFactory.ForAuctionOrder(NewAuctionOrder(AuctionAddressJson, 750));

        var result = await client.CreateShipmentAsync(request);

        Assert.Equal("GHN1234567", result.TrackingCode);
        Assert.Equal("GHN", result.CarrierCode);
        Assert.Equal("https://labels.test/GHN1234567.pdf", result.LabelPrintUrl);
        Assert.Equal("/api/v1/internal/shipments", handler.Path);
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal(request.OrderId, body.GetProperty("orderId").GetGuid());
        Assert.Equal(request.SubOrderId, body.GetProperty("subOrderId").GetGuid());
        Assert.Equal(request.SellerId, body.GetProperty("sellerId").GetGuid());
        Assert.Equal("E2E Buyer", body.GetProperty("recipientName").GetString());
        Assert.Equal("0912345678", body.GetProperty("recipientPhone").GetString());
        Assert.Equal(1450, body.GetProperty("toDistrictId").GetInt32());
        Assert.Equal("20804", body.GetProperty("toWardCode").GetString());
        Assert.Equal(750, body.GetProperty("weightInGrams").GetInt32());
        Assert.Equal(1, body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Client_WhenFulfillmentRefusesTheRequest_ReportsARejection_NotAnOutage()
    {
        var client = CreateClient(new CapturingHandler((HttpStatusCode.BadRequest, """{"errorCode":"VAL_INVALID_INPUT"}""")));

        var ex = await Assert.ThrowsAsync<CommerceDomainException>(() => client.CreateShipmentAsync(
            ShipmentRequestFactory.ForAuctionOrder(NewAuctionOrder(AuctionAddressJson, 750))));

        Assert.Equal("SHIPMENT_CREATION_REJECTED", ex.ErrorCode);
    }

    [Fact]
    public async Task Client_WhenFulfillmentIsDown_ReportsUnavailable()
    {
        var client = CreateClient(new CapturingHandler((HttpStatusCode.BadGateway, "")));

        var ex = await Assert.ThrowsAsync<CommerceDomainException>(() => client.CreateShipmentAsync(
            ShipmentRequestFactory.ForAuctionOrder(NewAuctionOrder(AuctionAddressJson, 750))));

        Assert.Equal("FULFILLMENT_SERVICE_UNAVAILABLE", ex.ErrorCode);
    }

    private static AuctionOrder NewAuctionOrder(string? addressJson, int weight) => new()
    {
        Id = Guid.NewGuid(), OrderNo = "AO-1", AuctionId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
        WinningBidId = Guid.NewGuid(), ProductId = Guid.NewGuid(), SkuId = Guid.NewGuid(), ProductNameSnapshot = "Helmet",
        WinningAmount = 1000000m, SellerReceiveAmount = 950000m, PlatformFeeAmount = 50000m,
        Status = SubOrderStatus.Paid, PaymentMethod = PaymentMethod.Wallet, PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(1),
        ShippingAddress = addressJson, ContactPhone = "0912345678", WeightGramsSnapshot = weight
    };

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
