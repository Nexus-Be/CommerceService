namespace Commerce.UnitTests.Infrastructure;

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Commerce.Api.Middleware;
using FluentAssertions;
using Moq;
using Xunit;

public class GlobalExceptionHandlerMiddlewareTests
{
    private readonly Mock<ILogger<GlobalExceptionHandlerMiddleware>> _mockLogger = new();

    [Fact]
    public async Task InvokeAsync_ShouldReturn409Conflict_WhenDbUpdateConcurrencyExceptionThrown()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        RequestDelegate next = (ctx) => throw new DbUpdateConcurrencyException("Concurrency conflict in test");

        var middleware = new GlobalExceptionHandlerMiddleware(next, _mockLogger.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.ContentType.Should().Be("application/problem+json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();

        using var jsonDoc = JsonDocument.Parse(responseBody);
        var root = jsonDoc.RootElement;

        root.GetProperty("status").GetInt32().Should().Be(409);
        root.GetProperty("errorCode").GetString().Should().Be("CONCURRENCY_CONFLICT");
        root.GetProperty("title").GetString().Should().Be("Conflict");
        root.GetProperty("detail").GetString().Should().Be("The resource was modified or deleted by another operation. Please refresh and try again.");
        root.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.10");
    }
}
