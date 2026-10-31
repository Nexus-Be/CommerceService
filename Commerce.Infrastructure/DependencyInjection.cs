namespace Commerce.Infrastructure;

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Commerce.Application.Interfaces;
using Commerce.Infrastructure.Persistence;
using Commerce.Infrastructure.Services.Catalog;
using Commerce.Infrastructure.Services.Wallet;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var rawConnection = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["CONNECTION_STRING"]
            ?? configuration["COMMERCE_CONNECTION_STRING"]
            ?? configuration["COMMERCE_DB_CONNECTION"]
            ?? Environment.GetEnvironmentVariable("COMMERCE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(rawConnection) || rawConnection.Contains("REPLACE_WITH"))
        {
            throw new InvalidOperationException("Database connection string for CommerceService is not configured. Set 'ConnectionStrings:DefaultConnection' or environment variable 'COMMERCE_CONNECTION_STRING'.");
        }
        var connectionString = NormalizeConnectionString(rawConnection);

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<ICommerceDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // REST Wallet Client
        var walletBaseUrl = Environment.GetEnvironmentVariable("WALLET_SERVICE_URL")
            ?? configuration["WALLET_SERVICE_URL"]
            ?? configuration["Services:Wallet:BaseUrl"]
            ?? configuration["WalletService:BaseUrl"]
            ?? "http://localhost:8008/";

        services.AddTransient<Services.Common.InternalServiceAuthHandler>();
        services.AddHttpClient<IWalletClient, WalletClient>(client =>
        {
            client.BaseAddress = new Uri(walletBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddHttpMessageHandler<Services.Common.InternalServiceAuthHandler>();

        // REST Catalog Client
        var catalogBaseUrl = Environment.GetEnvironmentVariable("CATALOG_SERVICE_URL")
            ?? configuration["CATALOG_SERVICE_URL"]
            ?? configuration["Services:Catalog:BaseUrl"]
            ?? configuration["CatalogService:BaseUrl"]
            ?? "http://localhost:8002/";

        services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
        {
            client.BaseAddress = new Uri(catalogBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddHttpMessageHandler<Services.Common.InternalServiceAuthHandler>();

        return services;
    }

    private static string NormalizeConnectionString(string? connStr)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return string.Empty;
        if (connStr.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || 
            connStr.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(connStr);
            var userInfo = uri.UserInfo.Split(':');
            var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 5432;
            var db = uri.AbsolutePath.TrimStart('/');
            return $"Host={host};Port={port};Database={db};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true";
        }
        return connStr;
    }
}
