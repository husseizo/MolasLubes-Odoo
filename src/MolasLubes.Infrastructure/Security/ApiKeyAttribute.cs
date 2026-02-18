using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAttribute : Attribute, IAsyncActionFilter
{
    private const string HEADER_NAME = "X-API-KEY";

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<ApiKeyOptions>>();

        if (!context.HttpContext.Request.Headers.TryGetValue(HEADER_NAME, out var apiKeyHeader))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                Message = "API Key is missing"
            });
            return;
        }

        var providedKey = apiKeyHeader.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(providedKey) ||
            providedKey != options.Value.ApiKey)
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                Message = "Invalid API Key"
            });
            return;
        }

        await next();
    }
}