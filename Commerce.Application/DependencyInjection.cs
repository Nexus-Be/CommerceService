namespace Commerce.Application;

using Microsoft.Extensions.DependencyInjection;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.AuctionOrder;
using Commerce.Application.Services.Cart;
using Commerce.Application.Services.Discount;
using Commerce.Application.Services.Order;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IAuctionOrderService, AuctionOrderService>();
        services.AddSingleton<IVoucherCalculationEngine, VoucherCalculationEngine>();
        services.AddScoped<IVoucherLifecycleService, VoucherLifecycleService>();
        services.AddScoped<IVoucherDiscountCalculator, VoucherDiscountCalculator>();
        services.AddScoped<IDiscountService, DiscountService>();
        return services;
    }
}
