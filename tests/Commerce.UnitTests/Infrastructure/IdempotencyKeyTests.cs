namespace Commerce.UnitTests.Infrastructure;

using System;
using FluentAssertions;
using Xunit;
using Commerce.Domain.Entities;

public class IdempotencyKeyTests
{
    [Fact]
    public void IdempotencyKey_ShouldDefaultExpiresAt_To24HoursInFuture()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var key = new IdempotencyKey
        {
            Id = Guid.NewGuid(),
            Key = Guid.NewGuid().ToString(),
            Scope = "CHECKOUT_GROUP_CREATE",
            ResponseStatusCode = 200,
            ResponseBody = "{\"status\":\"ok\"}"
        };

        var after = DateTimeOffset.UtcNow;

        // Assert
        key.ExpiresAt.Should().BeAfter(before.AddHours(23));
        key.ExpiresAt.Should().BeBefore(after.AddHours(25));
        key.IsDeleted.Should().BeFalse();
    }
}
