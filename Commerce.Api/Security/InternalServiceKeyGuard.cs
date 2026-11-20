namespace Commerce.Api.Security;

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Checks the X-Internal-Service-Key header of a service-to-service call. There is no built-in key: without configuration
/// every caller is refused (503), a missing or wrong key is 401, and the comparison takes the same time for every key.
/// </summary>
public static class InternalServiceKeyGuard
{
    public static IActionResult? Check(HttpRequest request, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var expected = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
            ?? configuration["INTERNAL_SERVICE_SECRET"]
            ?? configuration["InternalSecurity:ServiceKey"];

        if (string.IsNullOrWhiteSpace(expected))
        {
            return new ObjectResult(new
            {
                errorCode = "INTERNAL_CONFIGURATION_ERROR",
                message = "Internal service authentication is not configured."
            })
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }

        if (!request.Headers.TryGetValue("X-Internal-Service-Key", out var provided) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided.ToString()), Encoding.UTF8.GetBytes(expected)))
        {
            return new ObjectResult(new
            {
                errorCode = "INTERNAL_KEY_INVALID",
                message = "Internal service authentication is required."
            })
            { StatusCode = StatusCodes.Status401Unauthorized };
        }

        return null;
    }
}
