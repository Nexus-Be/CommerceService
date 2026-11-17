using Microsoft.Extensions.DependencyInjection.Extensions;
using Commerce.Api.Common;
using Commerce.Api.Extensions;
using Commerce.Api.Workers;
using Commerce.Application;
using Commerce.Infrastructure;
using Commerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Auxiliary Service Defaults (Observability, Swagger, JSON options)
builder.Services.AddServiceDefaults(builder.Configuration, "commerce-service");

// Configure Kestrel endpoint defaults for HTTP/1 & HTTP/2
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
    });
});

// 2. Application & Infrastructure Dependency Injection
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddHostedService<PaymentTimeoutWorker>();
builder.Services.AddHostedService<AuctionCallbackRetryWorker>();
builder.Services.AddHostedService<AuctionFinancialRetryWorker>();

// Notifications of paid regular orders: written to the notification outbox in the same SaveChanges as the payment, sent afterwards by this worker.
// Off by default (Notification:Enabled). The Notification service is called directly with this service's identity headers.
var notificationOptions = CommerceNotificationOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(notificationOptions);
builder.Services.AddSingleton<Commerce.Application.Interfaces.INotificationSettings>(notificationOptions);
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddHttpClient(NotificationOutboxWorker.HttpClientName, client =>
{
    client.BaseAddress = new Uri(notificationOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHostedService<NotificationOutboxWorker>();

// 3. REST Services
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ApiValidationProblemFactory.Create;
    });

var app = builder.Build();

// 4. Default Middleware Pipeline (Exception Handler, Swagger UI)
app.UseServiceDefaults();

// Automatically apply database migrations on startup if relational DB is available
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        if (db.Database.IsRelational())
        {
            db.Database.Migrate();
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Database migration deferred on startup: {Message}", ex.Message);
    }
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "commerce-service" }));

app.Run();
