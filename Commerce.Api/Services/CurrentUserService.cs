namespace Commerce.Api.Services;

using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Commerce.Application.Interfaces;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return null;

            // 1. Try ClaimTypes.NameIdentifier / sub
            var claimValue = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? httpContext.User.FindFirst("sub")?.Value;

            if (Guid.TryParse(claimValue, out var claimGuid))
            {
                return claimGuid;
            }

            // 2. Fallback to Gateway Header X-User-Id
            if (httpContext.Request.Headers.TryGetValue("X-User-Id", out var headerValue) &&
                Guid.TryParse(headerValue.FirstOrDefault(), out var headerGuid))
            {
                return headerGuid;
            }

            return null;
        }
    }

    public string? Role =>
        _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Role)?.Value
        ?? _httpContextAccessor.HttpContext?.Request.Headers["X-User-Role"].FirstOrDefault();

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true || UserId.HasValue;
}
