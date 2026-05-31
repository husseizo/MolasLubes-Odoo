using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MolasLubes.Infrastructure.Services.Notifications;

public sealed class ApnsNotificationSender
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly ApnsOptions _options;
    private readonly ILogger<ApnsNotificationSender> _logger;

    private readonly object _jwtSync = new();
    private string? _cachedJwt;
    private DateTime _cachedJwtExpiresAtUtc = DateTime.MinValue;
    private ECDsa? _cachedPrivateKey;

    public ApnsNotificationSender(
        HttpClient httpClient,
        IOptions<ApnsOptions> options,
        ILogger<ApnsNotificationSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured() => _options.IsConfigured;

    public async Task<ApnsSendResult> SendAsync(
        string deviceToken,
        object payload,
        CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return ApnsSendResult.NotConfigured("APNS is not configured.");

        var authJwt = GetOrCreateAuthJwt();
        var endpoint = _options.IsProduction
            ? "https://api.push.apple.com"
            : "https://api.development.push.apple.com";
        var uri = $"{endpoint}/3/device/{deviceToken}";

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.TryAddWithoutValidation("authorization", $"bearer {authJwt}");
        request.Headers.TryAddWithoutValidation("apns-topic", _options.BundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
                return ApnsSendResult.Success();

            var reason = ParseReason(body);
            _logger.LogWarning(
                "APNS send failed | StatusCode={Code} | Reason={Reason}",
                (int)response.StatusCode, reason ?? "-");

            return ApnsSendResult.Failed((int)response.StatusCode, reason ?? "APNS send failed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "APNS send threw for token ending with {Suffix}", SafeSuffix(deviceToken));
            return ApnsSendResult.Failed(0, ex.Message);
        }
    }

    private string GetOrCreateAuthJwt()
    {
        lock (_jwtSync)
        {
            var nowUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(_cachedJwt) && nowUtc < _cachedJwtExpiresAtUtc)
                return _cachedJwt;

            _cachedPrivateKey ??= LoadPrivateKey(_options.AuthKeyPath);

            var headerJson = JsonSerializer.Serialize(new { alg = "ES256", kid = _options.KeyId });
            var payloadJson = JsonSerializer.Serialize(new
            {
                iss = _options.TeamId,
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

            var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
            var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
            var signingInput = $"{headerB64}.{payloadB64}";

            var signatureRaw = _cachedPrivateKey.SignData(
                Encoding.UTF8.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            var signatureB64 = Base64UrlEncode(signatureRaw);

            _cachedJwt = $"{signingInput}.{signatureB64}";
            _cachedJwtExpiresAtUtc = nowUtc.AddMinutes(50);

            return _cachedJwt;
        }
    }

    private static ECDsa LoadPrivateKey(string path)
    {
        var pem = File.ReadAllText(path);
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string? ParseReason(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("reason", out var reasonEl) &&
                reasonEl.ValueKind == JsonValueKind.String)
            {
                return reasonEl.GetString();
            }
        }
        catch
        {
            // ignored
        }

        return responseBody.Length > 250 ? responseBody[..250] : responseBody;
    }

    private static string SafeSuffix(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "n/a";

        var trimmed = token.Trim();
        return trimmed.Length <= 8 ? trimmed : trimmed[^8..];
    }
}

public sealed class ApnsSendResult
{
    public bool IsSuccess { get; private set; }
    public int StatusCode { get; private set; }
    public string? Error { get; private set; }
    public bool IsPermanentTokenFailure { get; private set; }
    public bool IsNotConfigured { get; private set; }

    public static ApnsSendResult Success() => new()
    {
        IsSuccess = true,
        StatusCode = 200
    };

    public static ApnsSendResult NotConfigured(string reason) => new()
    {
        IsSuccess = false,
        StatusCode = 0,
        Error = reason,
        IsNotConfigured = true
    };

    public static ApnsSendResult Failed(int statusCode, string error)
    {
        var permanentTokenErrors = new[]
        {
            "BadDeviceToken",
            "DeviceTokenNotForTopic",
            "Unregistered"
        };

        var isPermanent = permanentTokenErrors.Any(e =>
            error.Contains(e, StringComparison.OrdinalIgnoreCase));

        return new ApnsSendResult
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Error = error,
            IsPermanentTokenFailure = isPermanent
        };
    }
}
