using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class BearerTokenAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var authHeader = context.HttpContext.Request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Bearer token is required." });
            return;
        }

        var rawToken = authHeader["Bearer ".Length..].Trim();

        var tokenService = context.HttpContext.RequestServices
            .GetRequiredService<InternalTokenService>();

        var user = await tokenService.ResolveTokenAsync(rawToken);

        if (user == null || !user.IsActive)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Invalid or expired token." });
            return;
        }

        context.HttpContext.Items["CurrentUser"] = user;
        context.HttpContext.Items["CurrentToken"] = rawToken;

        await next();
    }
}
