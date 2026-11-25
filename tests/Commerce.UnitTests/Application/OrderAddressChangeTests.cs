namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.AuctionOrder;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class OrderAddressChangeTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = FulfillmentMocks.WithConfirmedLocations();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IAuctionClient> _mockAuctionClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockOrderLogger = new();
    private readonly Mock<ILogger<AuctionOrderService>> _mockAuctionLogger = new();
    private readonly IDistributedCache _cache;
    private readonly OrderService _orderService;
    private readonly AuctionOrderService _auctionOrderService;

    public OrderAddressChangeTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        _orderService = new OrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockUserClient.Object,
            _cache,
            _mockOrderLogger.Object
        );

        _auctionOrderService = new AuctionOrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockAuctionClient.Object,
            _mockCatalogClient.Object,
            _mockUserClient.Object,
            _mockAuctionLogger.Object
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ChangeSubOrderShippingAddress_WhenPendingPayment_RecalculatesFee_AndUpdatesTotalAmount()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.PendingPayment,
            TotalAmount = 130000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrderId = Guid.NewGuid();
        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SO-101",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901112222",
            ItemsAmount = 100000m,
            ShippingFeeAmount = 30000m,
            TotalAmount = 130000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            Items = new List<SubOrderItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = subOrderId,
                    SellerId = sellerId,
                    SkuId = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    ProductNameSnapshot = "Product 1",
                    Quantity = 1,
                    UnitPrice = 100000m,
                    UnitPriceAfterDiscount = 100000m,
                    LineTotal = 100000m
                }
            }
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var newAddress = new ShippingAddressInput(
            ReceiverName: "New Receiver",
            ContactPhone: "0909999888",
            Street: "123 New St",
            Ward: "Ward 1",
            District: "District 1",
            Province: "HCM",
            GhnDistrictId: 1442,
            GhnWardCode: "20101", ProvinceId: 202
        );

        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(35000m); // Fee increases from 30000 to 35000 on pending payment

        // GHN names the codes differently from what was typed: the order must keep the GHN names.
        _mockFulfillmentClient
            .Setup(f => f.ValidateLocationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LocationCheckResult.Confirmed("Hồ Chí Minh", "Quận 1", "Phường Bến Nghé"));

        var request = new ChangeShippingAddressRequest(ShippingAddress: newAddress);

        // Act
        var result = await _orderService.ChangeSubOrderShippingAddressAsync(buyerId, subOrder.Id, request);

        // Assert
        result.Should().NotBeNull();
        result.OldShippingFee.Should().Be(30000m);
        result.NewShippingFee.Should().Be(35000m);
        result.TotalAmount.Should().Be(135000m);

        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.ShippingFeeAmount.Should().Be(35000m);
        updatedSubOrder.TotalAmount.Should().Be(135000m);
        updatedSubOrder.ShippingAddress.ReceiverName.Should().Be("New Receiver");
        updatedSubOrder.ShippingAddress.Province.Should().Be("Hồ Chí Minh", "the order keeps the name GHN gave, not the typed one");
        updatedSubOrder.ShippingAddress.District.Should().Be("Quận 1");
        updatedSubOrder.ShippingAddress.Ward.Should().Be("Phường Bến Nghé");
        updatedSubOrder.ShippingAddress.ProvinceId.Should().Be(202);
        updatedSubOrder.ShippingAddress.FullAddressText.Should().Contain("Phường Bến Nghé").And.NotContain("Ward 1");
        updatedSubOrder.ContactPhone.Should().Be("0909999888");

        var updatedOrder = await _dbContext.Orders.FindAsync(order.Id);
        updatedOrder!.TotalAmount.Should().Be(135000m);
    }

    [Fact]
    public async Task ChangeSubOrderShippingAddress_WhenPaid_AndFeeIsLowerOrEqual_Succeeds_WithoutFeeIncrease()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 140000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrderId = Guid.NewGuid();
        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SO-102",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901112222",
            ItemsAmount = 100000m,
            ShippingFeeAmount = 40000m,
            TotalAmount = 140000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            Items = new List<SubOrderItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = subOrderId,
                    SellerId = sellerId,
                    SkuId = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    ProductNameSnapshot = "Product 1",
                    Quantity = 1,
                    UnitPrice = 100000m,
                    UnitPriceAfterDiscount = 100000m,
                    LineTotal = 100000m
                }
            }
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var newAddress = new ShippingAddressInput(
            ReceiverName: "New Receiver",
            ContactPhone: "0909999888",
            Street: "123 New St",
            Ward: "Ward 1",
            District: "District 1",
            Province: "HCM",
            GhnDistrictId: 1442,
            GhnWardCode: "20101", ProvinceId: 202
        );

        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(30000m); // Fee is 30000 <= 40000

        var request = new ChangeShippingAddressRequest(ShippingAddress: newAddress);

        // Act
        var result = await _orderService.ChangeSubOrderShippingAddressAsync(buyerId, subOrder.Id, request);

        // Assert
        result.Should().NotBeNull();
        result.OldShippingFee.Should().Be(40000m);
        result.NewShippingFee.Should().Be(30000m);
        result.TotalAmount.Should().Be(140000m); // Prepaid total remains unchanged

        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.ShippingAddress.ReceiverName.Should().Be("New Receiver");
    }

    [Fact]
    public async Task ChangeSubOrderShippingAddress_WhenPaid_AndFeeIncreases_ThrowsShippingFeeIncreased_400()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 125000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrderId = Guid.NewGuid();
        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SO-103",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901112222",
            ItemsAmount = 100000m,
            ShippingFeeAmount = 25000m,
            TotalAmount = 125000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            Items = new List<SubOrderItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = subOrderId,
                    SellerId = sellerId,
                    SkuId = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    ProductNameSnapshot = "Product 1",
                    Quantity = 1,
                    UnitPrice = 100000m,
                    UnitPriceAfterDiscount = 100000m,
                    LineTotal = 100000m
                }
            }
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var newAddress = new ShippingAddressInput(
            ReceiverName: "New Receiver",
            ContactPhone: "0909999888",
            Street: "123 New St",
            Ward: "Ward 1",
            District: "District 1",
            Province: "HCM",
            GhnDistrictId: 1442,
            GhnWardCode: "20101", ProvinceId: 202
        );

        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(35000m); // Fee increases: 35000 > 25000 on Paid order

        var request = new ChangeShippingAddressRequest(ShippingAddress: newAddress);

        // Act
        var act = async () => await _orderService.ChangeSubOrderShippingAddressAsync(buyerId, subOrder.Id, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("SHIPPING_FEE_INCREASED");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ChangeSubOrderShippingAddress_WhenShipped_ThrowsCannotChangeAddressAfterShipping_400()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SO-104",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Shipping,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901112222",
            TrackingCode = "GHN123456",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var request = new ChangeShippingAddressRequest(ShippingAddress: new ShippingAddressInput(
            ReceiverName: "New Receiver",
            ContactPhone: "0909999888",
            Street: "123 New St",
            Ward: "Ward 1",
            District: "District 1",
            Province: "HCM",
            GhnDistrictId: 1442,
            GhnWardCode: "20101", ProvinceId: 202
        ));

        // Act
        var act = async () => await _orderService.ChangeSubOrderShippingAddressAsync(buyerId, subOrder.Id, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("CANNOT_CHANGE_ADDRESS_AFTER_SHIPPING");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ChangeSubOrderShippingAddress_WhenNotOwner_ThrowsForbidden_403()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.PendingPayment,
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SO-105",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901112222",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var request = new ChangeShippingAddressRequest(AddressId: Guid.NewGuid());

        // Act
        var act = async () => await _orderService.ChangeSubOrderShippingAddressAsync(otherUserId, subOrder.Id, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("FORBIDDEN");
        ex.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task ChangeAuctionShippingAddress_WhenPendingPayment_UpdatesAddressAndFee()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-20261119-01",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Coin",
            WinningAmount = 500000m,
            RemainingAmount = 500000m,
            SellerReceiveAmount = 475000m,
            PlatformFeeAmount = 25000m,
            WeightGramsSnapshot = 500,
            PaymentMethod = PaymentMethod.Wallet,
            Status = SubOrderStatus.PendingPayment,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        var newAddress = new ShippingAddressInput(
            ReceiverName: "Auction Winner",
            ContactPhone: "0908887777",
            Street: "456 Auction Blvd",
            Ward: "Ward 2",
            District: "District 2",
            Province: "HCM",
            GhnDistrictId: 1443,
            GhnWardCode: "20102", ProvinceId: 202
        );

        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(32000m);

        // GHN names the codes differently from what was typed: the order must keep the GHN names.
        _mockFulfillmentClient
            .Setup(f => f.ValidateLocationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LocationCheckResult.Confirmed("Hồ Chí Minh", "Quận 1", "Phường Bến Nghé"));

        var request = new ChangeShippingAddressRequest(ShippingAddress: newAddress);

        // Act
        var result = await _auctionOrderService.ChangeAuctionShippingAddressAsync(buyerId, auctionOrder.Id, request);

        // Assert
        result.Should().NotBeNull();
        result.TargetType.Should().Be("AuctionOrder");
        result.NewShippingFee.Should().Be(32000m);
        result.TotalAmount.Should().Be(532000m);

        var updated = await _dbContext.AuctionOrders.FindAsync(auctionOrder.Id);
        updated!.ShippingFeeAmount.Should().Be(32000m);
        updated.TotalAmount.Should().Be(532000m);
        updated.ContactPhone.Should().Be("0908887777");
        var stored = System.Text.Json.JsonSerializer.Deserialize<ShippingAddressDto>(updated.ShippingAddress!, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        stored.ProvinceName.Should().Be("Hồ Chí Minh", "the auction order keeps the names GHN gave");
        stored.DistrictName.Should().Be("Quận 1");
        stored.WardName.Should().Be("Phường Bến Nghé");
        stored.ProvinceId.Should().Be(202);
    }

    [Fact]
    public async Task ChangeAuctionShippingAddress_WhenPaid_AndFeeIncreases_ThrowsShippingFeeIncreased_400()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-20261119-02",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Watch",
            WinningAmount = 1000000m,
            RemainingAmount = 1000000m,
            SellerReceiveAmount = 950000m,
            PlatformFeeAmount = 50000m,
            WeightGramsSnapshot = 500,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingFeeAmount = 30000m,
            TotalAmount = 1030000m,
            Status = SubOrderStatus.Paid,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        var newAddress = new ShippingAddressInput(
            ReceiverName: "Auction Winner",
            ContactPhone: "0908887777",
            Street: "456 Auction Blvd",
            Ward: "Ward 2",
            District: "District 2",
            Province: "HCM",
            GhnDistrictId: 1443,
            GhnWardCode: "20102", ProvinceId: 202
        );

        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(45000m); // Fee increases: 45000 > 30000

        var request = new ChangeShippingAddressRequest(ShippingAddress: newAddress);

        // Act
        var act = async () => await _auctionOrderService.ChangeAuctionShippingAddressAsync(buyerId, auctionOrder.Id, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("SHIPPING_FEE_INCREASED");
        ex.Which.StatusCode.Should().Be(400);
    }
}
