namespace Commerce.UnitTests.Security;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Commerce.Api.Middleware;
using Commerce.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class InternalServiceSecurityTests
{
    private const string ValidKey = "unit-test-valid-internal-secret-key-2026";

    private static IConfiguration CreateConfiguration(string? key = null)
    {
        var dict = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(key))
        {
            dict["InternalSecurity:ServiceKey"] = key;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Fact]
    public void Guard_WhenConfigurationMissing_Returns503ServiceUnavailable()
    {
        // Arrange: Environment variable cleaned and empty configuration
        var originalEnv = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("INTERNAL_SERVICE_SECRET", null);
            var emptyConfig = CreateConfiguration(null);

            var context = new DefaultHttpContext();
            context.Request.Headers["X-Internal-Service-Key"] = "some-key";

            // Act
            var result = InternalServiceKeyGuard.Check(context.Request, emptyConfig);

            // Assert
            result.Should().NotBeNull();
            var objResult = result.Should().BeOfType<ObjectResult>().Subject;
            objResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);

            var serialized = System.Text.Json.JsonSerializer.Serialize(objResult.Value);
            serialized.Should().Contain("INTERNAL_CONFIGURATION_ERROR");
            serialized.Should().Contain("Internal service authentication is not configured.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("INTERNAL_SERVICE_SECRET", originalEnv);
        }
    }

    [Fact]
    public void Guard_WhenHeaderMissing_Returns401Unauthorized()
    {
        // Arrange
        var config = CreateConfiguration(ValidKey);
        var context = new DefaultHttpContext();

        // Act
        var result = InternalServiceKeyGuard.Check(context.Request, config);

        // Assert
        result.Should().NotBeNull();
        var objResult = result.Should().BeOfType<ObjectResult>().Subject;
        objResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var serialized = System.Text.Json.JsonSerializer.Serialize(objResult.Value);
        serialized.Should().Contain("INTERNAL_KEY_INVALID");
        serialized.Should().Contain("Internal service authentication is required.");
    }

    [Fact]
    public void Guard_WhenHeaderWrong_Returns401Unauthorized()
    {
        // Arrange
        var config = CreateConfiguration(ValidKey);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Internal-Service-Key"] = "incorrect-secret-key";

        // Act
        var result = InternalServiceKeyGuard.Check(context.Request, config);

        // Assert
        result.Should().NotBeNull();
        var objResult = result.Should().BeOfType<ObjectResult>().Subject;
        objResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var serialized = System.Text.Json.JsonSerializer.Serialize(objResult.Value);
        serialized.Should().Contain("INTERNAL_KEY_INVALID");
    }

    [Fact]
    public void Guard_WhenHeaderValid_ReturnsNullAllowingExecution()
    {
        // Arrange
        var config = CreateConfiguration(ValidKey);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Internal-Service-Key"] = ValidKey;

        // Act
        var result = InternalServiceKeyGuard.Check(context.Request, config);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Middleware_WhenInternalRouteAndKeyNotConfigured_Returns503ServiceUnavailable()
    {
        // Arrange
        var originalEnv = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("INTERNAL_SERVICE_SECRET", null);
            var emptyConfig = CreateConfiguration(null);

            var nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var envMock = new Mock<IHostEnvironment>();
            envMock.Setup(e => e.EnvironmentName).Returns("Production");
            var loggerMock = new Mock<ILogger<InternalServiceAuthMiddleware>>();

            var middleware = new InternalServiceAuthMiddleware(next, emptyConfig, envMock.Object, loggerMock.Object);

            var context = new DefaultHttpContext();
            context.Request.Path = "/api/v1/internal/orders/dispute-eligibility";
            context.Response.Body = new MemoryStream();

            // Act
            await middleware.InvokeAsync(context);

            // Assert
            nextCalled.Should().BeFalse();
            context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
            body.Should().Contain("INTERNAL_CONFIGURATION_ERROR");
            body.Should().Contain("Internal service authentication is not configured.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("INTERNAL_SERVICE_SECRET", originalEnv);
        }
    }

    [Fact]
    public async Task Middleware_WhenInternalRouteAndKeyInvalid_Returns403Forbidden()
    {
        // Arrange
        var config = CreateConfiguration(ValidKey);
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Production");
        var loggerMock = new Mock<ILogger<InternalServiceAuthMiddleware>>();

        var middleware = new InternalServiceAuthMiddleware(next, config, envMock.Object, loggerMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/internal/orders/dispute-eligibility";
        context.Request.Headers["X-Internal-Service-Key"] = "wrong-key";
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("FORBIDDEN");
    }

    [Fact]
    public async Task Middleware_WhenInternalRouteAndKeyValid_InvokesNext()
    {
        // Arrange
        var config = CreateConfiguration(ValidKey);
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Production");
        var loggerMock = new Mock<ILogger<InternalServiceAuthMiddleware>>();

        var middleware = new InternalServiceAuthMiddleware(next, config, envMock.Object, loggerMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/internal/orders/dispute-eligibility";
        context.Request.Headers["X-Internal-Service-Key"] = ValidKey;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_WhenNonInternalRoute_PassesThroughWithoutAuth()
    {
        // Arrange
        var emptyConfig = CreateConfiguration(null);
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var envMock = new Mock<IHostEnvironment>();
        var loggerMock = new Mock<ILogger<InternalServiceAuthMiddleware>>();

        var middleware = new InternalServiceAuthMiddleware(next, emptyConfig, envMock.Object, loggerMock.Object);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/carts/me";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
    }
}
