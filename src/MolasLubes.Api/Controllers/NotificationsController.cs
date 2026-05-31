using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Application.Notifications;
using MolasLubes.Infrastructure.Services.Notifications;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class NotificationsController : ControllerBase
{
    private readonly LiquiMolyPushNotificationService _pushService;

    public NotificationsController(LiquiMolyPushNotificationService pushService)
    {
        _pushService = pushService;
    }

    [HttpPost("device-token")]
    public async Task<IActionResult> RegisterDeviceToken(
        [FromBody] RegisterDeviceTokenRequest request,
        CancellationToken ct)
    {
        try
        {
            var sapUserCode = GetCurrentSapUserCode();
            var result = await _pushService.RegisterDeviceTokenAsync(sapUserCode, request, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (ArgumentException ex) { return UnprocessableEntity(new { message = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
    }

    [HttpDelete("device-token")]
    public async Task<IActionResult> RemoveDeviceToken(
        [FromBody] RemoveDeviceTokenRequest? request,
        [FromQuery] string? deviceToken,
        CancellationToken ct)
    {
        try
        {
            var sapUserCode = GetCurrentSapUserCode();
            var result = await _pushService.RemoveDeviceTokenAsync(sapUserCode, request, deviceToken, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (ArgumentException ex) { return UnprocessableEntity(new { message = ex.Message }); }
        catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
    }

    private string GetCurrentSapUserCode()
    {
        var sapUserCode = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(sapUserCode))
            throw new UnauthorizedAccessException("Authenticated SAP user was not found in the access token.");

        return sapUserCode.Trim();
    }
}
