namespace MolasLubes.Application.Notifications;

public class RegisterDeviceTokenRequest
{
    public string Platform { get; set; } = "ios";
    public string DeviceToken { get; set; } = string.Empty;
    public string SapUserCode { get; set; } = string.Empty;
    public string BundleId { get; set; } = string.Empty;
    public string? AppBuild { get; set; }
    public string? AppVersion { get; set; }
}

public class RemoveDeviceTokenRequest
{
    public string? DeviceToken { get; set; }
    public string? Platform { get; set; }
    public string? BundleId { get; set; }
}

public class DeviceTokenMutationResponse
{
    public bool Success { get; set; }
    public int Affected { get; set; }
    public string Message { get; set; } = string.Empty;
}
