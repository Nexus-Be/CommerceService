namespace Commerce.Application;

using Microsoft.Extensions.DependencyInjection;
using Commerce.Application.Services.Cart;
using Commerce.Application.Services.Order;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        return services;
    }
}
