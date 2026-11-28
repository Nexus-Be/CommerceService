namespace Commerce.UnitTests.Infrastructure;

using System;
using System.Collections.Generic;
using Commerce.Infrastructure.Services.Fulfillment;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

public class FulfillmentTimeoutTests
{
    private static IConfiguration Config(string? value) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["FULFILLMENT_TIMEOUT_SECONDS"] = value }).Build();

    [Fact]
    public void Without_a_setting_the_wait_stays_thirty_seconds()
    {
        Environment.SetEnvironmentVariable("FULFILLMENT_TIMEOUT_SECONDS", null);
        FulfillmentTimeout.Resolve(Config(null)).Should().Be(TimeSpan.FromSeconds(30));
        FulfillmentTimeout.Resolve(null).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("1", 1)]
    [InlineData("120", 120)]
    public void A_valid_setting_is_used(string value, int expected)
    {
        Environment.SetEnvironmentVariable("FULFILLMENT_TIMEOUT_SECONDS", null);
        FulfillmentTimeout.Resolve(Config(value)).Should().Be(TimeSpan.FromSeconds(expected));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("121")]
    [InlineData("abc")]
    [InlineData("")]
    public void An_unusable_setting_falls_back_to_the_default(string value)
    {
        Environment.SetEnvironmentVariable("FULFILLMENT_TIMEOUT_SECONDS", null);
        FulfillmentTimeout.Resolve(Config(value)).Should().Be(TimeSpan.FromSeconds(30));
    }
}
