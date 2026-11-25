namespace Commerce.UnitTests;

using Commerce.Api.Controllers;
using Commerce.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

/// <summary>
/// OrdersController demands a signed-in user on every action. The delivery webhook is called by Fulfillment, which has no user,
/// so before this every delivery update was answered with 401 and no order ever moved past "confirmed".
/// </summary>
public sealed class RequireRoleAllowAnonymousTests
{
    [Fact]
    public void ActionWithoutAllowAnonymous_StillRequiresASignedInUser()
    {
        var context = NewContext(allowAnonymous: false);

        new RequireRoleAttribute("buyer", "seller").OnAuthorization(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public void ActionMarkedAllowAnonymous_IsNotBlockedByTheRoleCheck()
    {
        var context = NewContext(allowAnonymous: true);

        new RequireRoleAttribute("buyer", "seller").OnAuthorization(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void OnlyTheServiceToServiceEndpoints_AreMarkedAllowAnonymous()
    {
        // Both have no user and are protected by the internal service key (checked inside the action); nothing else may skip the role check.
        var anonymousActions = typeof(OrdersController).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Length > 0)
            .Select(m => m.Name)
            .OrderBy(name => name)
            .ToList();

        Assert.Equal(
            new[]
            {
                nameof(OrdersController.DisputeRefundSubOrder),
                nameof(OrdersController.GetDisputeEligibility),
                nameof(OrdersController.GetReviewEligibility),
                nameof(OrdersController.RefundForDispute),
                nameof(OrdersController.UpdateDeliveryStatus)
            },
            anonymousActions);
    }

    private static AuthorizationFilterContext NewContext(bool allowAnonymous)
    {
        var descriptor = new ActionDescriptor();
        if (allowAnonymous)
            descriptor.EndpointMetadata = new List<object> { new AllowAnonymousAttribute() };

        return new AuthorizationFilterContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor),
            new List<IFilterMetadata>());
    }
}
