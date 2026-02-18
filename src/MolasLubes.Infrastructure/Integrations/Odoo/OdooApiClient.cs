using System.Net;
using System.Net.Http.Json;

public class OdooApiResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public HttpStatusCode StatusCode { get; init; }
}

public class OdooApiClient
{
    private readonly HttpClient _http;

    public OdooApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<OdooApiResult> PushDeliveryAsync(object payload)
        => await PostAsync("/api/deliveries", payload);

    public async Task<OdooApiResult> PushInvoiceAsync(object payload)
        => await PostAsync("/api/invoices", payload);

    public async Task<OdooApiResult> PushPaymentAsync(object payload)
        => await PostAsync("/api/payments", payload);

    private async Task<OdooApiResult> PostAsync(string url, object payload)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(url, payload);

            if (response.IsSuccessStatusCode)
            {
                return new OdooApiResult
                {
                    Success = true,
                    StatusCode = response.StatusCode
                };
            }

            var error = await response.Content.ReadAsStringAsync();

            return new OdooApiResult
            {
                Success = false,
                StatusCode = response.StatusCode,
                ErrorMessage = error
            };
        }
        catch (Exception ex)
        {
            return new OdooApiResult
            {
                Success = false,
                StatusCode = HttpStatusCode.InternalServerError,
                ErrorMessage = ex.Message
            };
        }
    }
}