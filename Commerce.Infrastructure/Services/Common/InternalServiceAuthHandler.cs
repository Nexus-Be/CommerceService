namespace Commerce.Infrastructure.Services.Common;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

public sealed class InternalServiceAuthHandler : DelegatingHandler
{
    private readonly IConfiguration _configuration;
    public const string HeaderKeyInternalService = "X-Internal-Service-Key";

    public InternalServiceAuthHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var serviceKey = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_SECRET")
            ?? _configuration["INTERNAL_SERVICE_SECRET"]
            ?? _configuration["InternalSecurity:ServiceKey"];

        if (!string.IsNullOrWhiteSpace(serviceKey) && !request.Headers.Contains(HeaderKeyInternalService))
        {
            request.Headers.Add(HeaderKeyInternalService, serviceKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
