namespace Commerce.UnitTests.Controllers;

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Controllers.Admin;
using Commerce.Api.Filters;
using Commerce.Application.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public class AdminPlatformFeeTests
{
    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;
    private readonly Mock<ILogger<AdminPlatformFeeController>> _mockLogger = new();

    public AdminPlatformFeeTests()
    {
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformFee:DefaultPercent"] = "0.0"
        });
        _configuration = configBuilder.Build();
    }

    private AdminPlatformFeeController CreateController(Guid? userId = null, string? role = null)
    {
        var controller = new AdminPlatformFeeController(_cache, Commerce.UnitTests.Common.TestDbContextFactory.CreateInMemoryDbContext(), _configuration, _mockLogger.Object);
        var httpContext = new DefaultHttpContext();

        if (userId.HasValue)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.Value.ToString()),
                new("sub", userId.Value.ToString())
            };

            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
                claims.Add(new Claim("role", role));
            }

            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            httpContext.Request.Headers["x-user-id"] = userId.Value.ToString();
            if (!string.IsNullOrEmpty(role))
            {
                httpContext.Request.Headers["x-user-roles"] = role;
            }
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    [Fact]
    public void RequireRoleFilter_ShouldReturn403Forbidden_WhenUserIsBuyer()
    {
        // Arrange
        var filter = new RequireRoleAttribute("staff", "admin");
        var httpContext = new DefaultHttpContext();
        var userId = Guid.NewGuid();
        httpContext.Request.Headers["x-user-id"] = userId.ToString();
        httpContext.Request.Headers["x-user-roles"] = "buyer";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "buyer")
        }, "TestAuth"));

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        // Act
        filter.OnAuthorization(filterContext);

        // Assert
        filterContext.Result.Should().NotBeNull();
        filterContext.Result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)filterContext.Result!;
        objResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void RequireRoleFilter_ShouldAllowAccess_WhenUserIsStaff()
    {
        // Arrange
        var filter = new RequireRoleAttribute("staff", "admin");
        var httpContext = new DefaultHttpContext();
        var userId = Guid.NewGuid();
        httpContext.Request.Headers["x-user-id"] = userId.ToString();
        httpContext.Request.Headers["x-user-roles"] = "staff";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "staff")
        }, "TestAuth"));

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        // Act
        filter.OnAuthorization(filterContext);

        // Assert
        filterContext.Result.Should().BeNull();
    }

    [Fact]
    public async Task GetPlatformFee_ShouldReturnConfigDefault_WhenCacheIsEmpty()
    {
        // Arrange
        var controller = CreateController(Guid.NewGuid(), "staff");

        // Act
        var result = await controller.GetPlatformFee(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value.Should().BeOfType<PlatformFeeResponse>().Subject;
        response.DefaultPercent.Should().Be(0.0m);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(100.5)]
    [InlineData(-50.0)]
    [InlineData(150.0)]
    public async Task UpdatePlatformFee_ShouldReturn400BadRequest_WhenRateIsOutOfRange(decimal invalidRate)
    {
        // Arrange
        var controller = CreateController(Guid.NewGuid(), "staff");
        var request = new UpdatePlatformFeeRequest(invalidRate);

        // Act
        var result = await controller.UpdatePlatformFee(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var objResult = (BadRequestObjectResult)result;
        objResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UpdatePlatformFee_ShouldUpdateCacheAndReturn200_WhenRateIsValid()
    {
        // Arrange
        var staffId = Guid.NewGuid();
        var controller = CreateController(staffId, "staff");
        var request = new UpdatePlatformFeeRequest(5.5m);

        // Act
        var updateResult = await controller.UpdatePlatformFee(request, CancellationToken.None);

        // Assert Update
        updateResult.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)updateResult;
        var response = okResult.Value.Should().BeOfType<PlatformFeeResponse>().Subject;
        response.DefaultPercent.Should().Be(5.5m);
        response.UpdatedByRole.Should().Be("staff");

        // Assert Subsequent Get
        var getResult = await controller.GetPlatformFee(CancellationToken.None);
        var okGetResult = (OkObjectResult)getResult;
        var getResponse = okGetResult.Value.Should().BeOfType<PlatformFeeResponse>().Subject;
        getResponse.DefaultPercent.Should().Be(5.5m);
        getResponse.UpdatedByRole.Should().Be("staff");
    }
}
