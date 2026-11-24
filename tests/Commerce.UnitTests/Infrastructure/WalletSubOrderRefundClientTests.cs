namespace Commerce.UnitTests.Infrastructure;

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Infrastructure.Services.Wallet;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

/// <summary>G1/G4: the sub-order refund sends the parent order id and tells a new credit from a replay of an existing one.</summary>
public class WalletSubOrderRefundClientTests
{
    private static (WalletClient Client, Func<string?> SentBody) Create(HttpResponseMessage? response, Exception? failure = null)
    {
        string? sentBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                sentBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
                if (failure is not null) throw failure;
                return response!;
            });
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:8008/") };
        return (new WalletClient(http, NullLogger<WalletClient>.Instance), () => sentBody);
    }

    [Fact]
    public async Task The_request_carries_the_parent_order_id_and_no_idempotency_key()
    {
        var transactionId = Guid.NewGuid();
        var (client, body) = Create(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { id = transactionId.ToString(), replayed = false }))
        });
        var parent = Guid.NewGuid();

        var result = await client.RefundSubOrderPaymentAsync(Guid.NewGuid(), parent, Guid.NewGuid().ToString(), 150000m, "Refund", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.TransactionId.Should().Be(transactionId);
        result.Replayed.Should().BeFalse();
        using var doc = JsonDocument.Parse(body()!);
        doc.RootElement.GetProperty("parentOrderId").GetGuid().Should().Be(parent);
        doc.RootElement.GetProperty("amount").GetDecimal().Should().Be(150000m);
        doc.RootElement.TryGetProperty("idempotencyKey", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_repeated_refund_is_reported_as_a_replay_with_the_existing_transaction()
    {
        var transactionId = Guid.NewGuid();
        var (client, _) = Create(new HttpResponseMessage(HttpStatusCode.OK)
        {
            // The answer may also be wrapped in a "data" envelope.
            Content = new StringContent(JsonSerializer.Serialize(new { data = new { id = transactionId.ToString(), replayed = true } }))
        });

        var result = await client.RefundSubOrderPaymentAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString(), 150000m, "Refund", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Replayed.Should().BeTrue();
        result.TransactionId.Should().Be(transactionId);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "FEATURE_DISABLED")]
    [InlineData(HttpStatusCode.BadRequest, "PARENT_ORDER_REQUIRED")]
    [InlineData(HttpStatusCode.Conflict, "REFUND_CONFLICT")]
    public async Task A_refusal_is_a_failure_that_keeps_the_wallet_error_code(HttpStatusCode status, string code)
    {
        var (client, _) = Create(new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { errorCode = code, detail = "refused" }))
        });

        var result = await client.RefundSubOrderPaymentAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString(), 150000m, "Refund", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(code);
        result.TransactionId.Should().BeNull();
    }

    [Fact]
    public async Task An_unreachable_wallet_is_a_failure_not_an_exception()
    {
        var (client, _) = Create(null, new HttpRequestException("connection refused"));

        var result = await client.RefundSubOrderPaymentAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString(), 150000m, "Refund", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("WALLET_UNREACHABLE");
    }
}
