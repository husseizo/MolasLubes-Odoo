using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Security;

/// <summary>
/// Accepts requests authenticated with either a bearer token (InternalUser) or
/// a static X-API-KEY header. When a valid bearer token is presented, the resolved
/// InternalUser is placed in HttpContext.Items["CurrentUser"] for downstream role checks.
/// If a bearer token is present but invalid, the request is rejected immediately (no
/// fallback to API key). If no bearer token is present, the API key is required.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAttribute : Attribute, IAsyncActionFilter
{
    private const string ApiKeyHeaderName = "X-API-KEY";

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        // ── Bearer token path ──────────────────────────────────────────────
        var authHeader = context.HttpContext.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
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
            return;
        }

        // ── API key fallback ───────────────────────────────────────────────
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<ApiKeyOptions>>();

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "API key is missing." });
            return;
        }

        var providedKey = apiKeyHeader.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedKey) || providedKey != options.Value.ApiKey)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Invalid API key." });
            return;
        }

        await next();
    }
}