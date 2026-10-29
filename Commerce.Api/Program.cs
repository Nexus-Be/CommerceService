using Commerce.Api.Extensions;
using Commerce.Api.Middleware;
using Commerce.Application;
using Commerce.Infrastructure;
using Commerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 0. Observability / OpenTelemetry Tracing
builder.Services.AddServiceObservability(builder.Configuration, "commerce-service");

// Cấu hình Kestrel chỉ định HTTP/1 & HTTP/2
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
    });
});

// 1. Application & Infrastructure Dependency Injection
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Commerce.Application.Interfaces.ICurrentUserService, Commerce.Api.Services.CurrentUserService>();
builder.Services.AddControllers();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// 2. Swagger / OpenAPI & JSON Serialization
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new Commerce.Api.Common.Converters.PascalCaseJsonStringEnumConverter(allowIntegerValues: false));
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new Commerce.Api.Common.Converters.PascalCaseJsonStringEnumConverter(allowIntegerValues: false));
});

var app = builder.Build();

// 3. Global Exception Handler Middleware
app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Commerce.Api" }));

app.Run();
