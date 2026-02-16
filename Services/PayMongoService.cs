using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CKNDocument.Services;

/// <summary>
/// Service for PayMongo payment gateway integration
/// Uses Sources API for e-wallet payments (GCash, GrabPay, Maya)
/// Uses environment variable PAYMONGO_SECRET_KEY or appsettings fallback
/// </summary>
public class PayMongoService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PayMongoService> _logger;
    private readonly string? _secretKey;
    private readonly string? _authHeaderValue;
    private const string BaseUrl = "https://api.paymongo.com/v1";

    public bool IsConfigured => !string.IsNullOrEmpty(_secretKey);

    /// <summary>
    /// All supported payment methods
    /// </summary>
    public static readonly Dictionary<string, string> SupportedMethods = new()
    {
        { "gcash", "GCash" },
        { "grab_pay", "GrabPay" },
        { "paymaya", "Maya" },
        { "card", "Credit / Debit Card" },
        { "dob", "Online Banking (BPI)" },
        { "dob_ubp", "Online Banking (UnionBank)" },
    };

    /// <summary>
    /// Methods that use the Sources API (e-wallets)
    /// </summary>
    public static readonly HashSet<string> SourceBasedMethods = new() { "gcash", "grab_pay" };

    /// <summary>
    /// Methods that use the Payment Intents API
    /// </summary>
    public static readonly HashSet<string> IntentBasedMethods = new() { "paymaya", "card", "dob", "dob_ubp" };

    public PayMongoService(HttpClient httpClient, ILogger<PayMongoService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;

        _secretKey = Environment.GetEnvironmentVariable("PAYMONGO_SECRET_KEY");
        if (string.IsNullOrEmpty(_secretKey))
            _secretKey = configuration["PayMongo:SecretKey"];

        if (string.IsNullOrEmpty(_secretKey))
        {
            _logger.LogWarning("PayMongo API key not configured. Payment processing unavailable.");
        }
        else
        {
            var authBytes = Encoding.UTF8.GetBytes($"{_secretKey}:");
            _authHeaderValue = Convert.ToBase64String(authBytes);
            _logger.LogInformation("PayMongo service initialized successfully.");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(_authHeaderValue))
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authHeaderValue);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>
    /// Create a PayMongo Source for e-wallet payment (GCash, GrabPay)
    /// This is the working flow that bypasses the Checkout Sessions issue
    /// </summary>
    public async Task<PayMongoSourceResult> CreateSource(
        decimal amount,
        string type,
        string successUrl,
        string failedUrl,
        string? description = null)
    {
        if (!IsConfigured)
        {
            return new PayMongoSourceResult
            {
                Success = false,
                ErrorMessage = "PayMongo is not configured. Please set the PAYMONGO_SECRET_KEY."
            };
        }

        if (!SourceBasedMethods.Contains(type))
        {
            return new PayMongoSourceResult
            {
                Success = false,
                ErrorMessage = $"Unsupported source method: {type}. Use CreatePaymentViaIntent for intent-based methods."
            };
        }

        try
        {
            var amountInCentavos = (int)(amount * 100);
            if (amountInCentavos < 100)
            {
                return new PayMongoSourceResult
                {
                    Success = false,
                    ErrorMessage = "Amount must be at least ₱1.00"
                };
            }

            var payload = new
            {
                data = new
                {
                    attributes = new
                    {
                        amount = amountInCentavos,
                        redirect = new
                        {
                            success = successUrl,
                            failed = failedUrl
                        },
                        type = type,
                        currency = "PHP",
                        description = description ?? "CKN Document Subscription Payment"
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            _logger.LogInformation("Creating PayMongo {Type} source for {Amount} centavos", type, amountInCentavos);

            var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/sources");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo Source API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                var errorMsg = ParseErrorMessage(responseBody) ?? $"PayMongo API error: {response.StatusCode}";
                return new PayMongoSourceResult { Success = false, ErrorMessage = errorMsg };
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var data = result.GetProperty("data");
            var attributes = data.GetProperty("attributes");
            var redirect = attributes.GetProperty("redirect");

            var sourceId = data.GetProperty("id").GetString()!;
            var checkoutUrl = redirect.GetProperty("checkout_url").GetString()!;

            _logger.LogInformation("PayMongo source created: {SourceId}, type: {Type}", sourceId, type);

            return new PayMongoSourceResult
            {
                Success = true,
                SourceId = sourceId,
                CheckoutUrl = checkoutUrl,
                Status = attributes.GetProperty("status").GetString() ?? "pending",
                Type = type
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error connecting to PayMongo");
            return new PayMongoSourceResult
            {
                Success = false,
                ErrorMessage = "Unable to connect to PayMongo. Please check your internet connection."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PayMongo source");
            return new PayMongoSourceResult
            {
                Success = false,
                ErrorMessage = $"Payment processing error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get the status of a source (pending, chargeable, cancelled, expired, failed)
    /// </summary>
    public async Task<PayMongoSourceStatus> GetSourceStatus(string sourceId)
    {
        if (!IsConfigured)
            return new PayMongoSourceStatus { Status = "error", ErrorMessage = "PayMongo not configured" };

        try
        {
            var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/sources/{sourceId}");
            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo Source status error: {StatusCode}", response.StatusCode);
                return new PayMongoSourceStatus { Status = "error" };
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var attributes = result.GetProperty("data").GetProperty("attributes");

            return new PayMongoSourceStatus
            {
                Status = attributes.GetProperty("status").GetString() ?? "pending",
                Type = attributes.GetProperty("type").GetString(),
                Amount = attributes.GetProperty("amount").GetInt64() / 100m
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting source status");
            return new PayMongoSourceStatus { Status = "error", ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Create a payment to charge a chargeable source
    /// </summary>
    public async Task<PayMongoPaymentResult> CreatePayment(string sourceId, decimal amount, string? description = null)
    {
        if (!IsConfigured)
            return new PayMongoPaymentResult { Success = false, ErrorMessage = "PayMongo not configured" };

        try
        {
            var amountInCentavos = (int)(amount * 100);
            var payload = new
            {
                data = new
                {
                    attributes = new
                    {
                        amount = amountInCentavos,
                        source = new
                        {
                            id = sourceId,
                            type = "source"
                        },
                        currency = "PHP",
                        description = description ?? "CKN Document Subscription Payment"
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            _logger.LogInformation("Creating PayMongo payment from source {SourceId}", sourceId);

            var request = CreateRequest(HttpMethod.Post, $"{BaseUrl}/payments");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo Payment API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                var errorMsg = ParseErrorMessage(responseBody) ?? $"Payment failed: {response.StatusCode}";
                return new PayMongoPaymentResult { Success = false, ErrorMessage = errorMsg };
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var data = result.GetProperty("data");
            var attributes = data.GetProperty("attributes");

            var paymentId = data.GetProperty("id").GetString()!;
            var status = attributes.GetProperty("status").GetString()!;
            var paidAt = attributes.TryGetProperty("paid_at", out var paidAtElem) && paidAtElem.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(paidAtElem.GetInt64()).DateTime
                : (DateTime?)null;

            string? paymentMethod = null;
            if (attributes.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
                paymentMethod = source.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            _logger.LogInformation("PayMongo payment {PaymentId} status: {Status}", paymentId, status);

            return new PayMongoPaymentResult
            {
                Success = status == "paid",
                PaymentId = paymentId,
                Status = status,
                PaymentMethod = paymentMethod,
                PaidAt = paidAt,
                Amount = attributes.GetProperty("amount").GetInt64() / 100m
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PayMongo payment");
            return new PayMongoPaymentResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Create a PayMongo Payment Intent + Payment Method + Attach for non-source payment methods
    /// Supports: paymaya, card, dob, dob_ubp
    /// </summary>
    public async Task<PayMongoSourceResult> CreatePaymentViaIntent(
        decimal amount,
        string type,
        string returnUrl,
        string? description = null,
        CardDetails? cardDetails = null)
    {
        if (!IsConfigured)
            return new PayMongoSourceResult { Success = false, ErrorMessage = "PayMongo is not configured." };

        if (!IntentBasedMethods.Contains(type))
            return new PayMongoSourceResult { Success = false, ErrorMessage = $"Unsupported intent method: {type}" };

        try
        {
            var amountInCentavos = (int)(amount * 100);
            if (amountInCentavos < 100)
                return new PayMongoSourceResult { Success = false, ErrorMessage = "Amount must be at least ₱1.00" };

            // Step 1: Create Payment Intent
            var intentPayload = new
            {
                data = new
                {
                    attributes = new
                    {
                        amount = amountInCentavos,
                        payment_method_allowed = new[] { type },
                        currency = "PHP",
                        description = description ?? "CKN Document Subscription Payment"
                    }
                }
            };

            var intentJson = JsonSerializer.Serialize(intentPayload);
            _logger.LogInformation("Creating PayMongo PaymentIntent for {Type}, {Amount} centavos", type, amountInCentavos);

            var intentRequest = CreateRequest(HttpMethod.Post, $"{BaseUrl}/payment_intents");
            intentRequest.Content = new StringContent(intentJson, Encoding.UTF8, "application/json");

            var intentResponse = await _httpClient.SendAsync(intentRequest);
            var intentBody = await intentResponse.Content.ReadAsStringAsync();

            if (!intentResponse.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo PI create error: {Code} - {Body}", intentResponse.StatusCode, intentBody);
                return new PayMongoSourceResult { Success = false, ErrorMessage = ParseErrorMessage(intentBody) ?? "Failed to create payment intent" };
            }

            var intentResult = JsonSerializer.Deserialize<JsonElement>(intentBody);
            var intentData = intentResult.GetProperty("data");
            var intentId = intentData.GetProperty("id").GetString()!;
            var clientKey = intentData.GetProperty("attributes").GetProperty("client_key").GetString()!;

            // Step 2: Create Payment Method (with details for banks/cards)
            object methodAttributes;

            if (type == "dob")
            {
                methodAttributes = new { type = type, details = new { bank_code = "bpi" } };
            }
            else if (type == "dob_ubp")
            {
                methodAttributes = new { type = type, details = new { bank_code = "ubp" } };
            }
            else if (type == "card" && cardDetails != null)
            {
                methodAttributes = new
                {
                    type = type,
                    details = new
                    {
                        card_number = cardDetails.CardNumber,
                        exp_month = cardDetails.ExpMonth,
                        exp_year = cardDetails.ExpYear,
                        cvc = cardDetails.Cvc
                    }
                };
            }
            else
            {
                methodAttributes = new { type = type };
            }

            var methodPayload = new { data = new { attributes = methodAttributes } };

            var methodJson = JsonSerializer.Serialize(methodPayload);
            var methodRequest = CreateRequest(HttpMethod.Post, $"{BaseUrl}/payment_methods");
            methodRequest.Content = new StringContent(methodJson, Encoding.UTF8, "application/json");

            var methodResponse = await _httpClient.SendAsync(methodRequest);
            var methodBody = await methodResponse.Content.ReadAsStringAsync();

            if (!methodResponse.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo PM create error: {Code} - {Body}", methodResponse.StatusCode, methodBody);
                return new PayMongoSourceResult { Success = false, ErrorMessage = ParseErrorMessage(methodBody) ?? "Failed to create payment method" };
            }

            var methodResult = JsonSerializer.Deserialize<JsonElement>(methodBody);
            var methodId = methodResult.GetProperty("data").GetProperty("id").GetString()!;

            // Step 3: Attach Payment Method to Payment Intent
            var attachPayload = new
            {
                data = new
                {
                    attributes = new
                    {
                        payment_method = methodId,
                        client_key = clientKey,
                        return_url = returnUrl
                    }
                }
            };

            var attachJson = JsonSerializer.Serialize(attachPayload);
            var attachRequest = CreateRequest(HttpMethod.Post, $"{BaseUrl}/payment_intents/{intentId}/attach");
            attachRequest.Content = new StringContent(attachJson, Encoding.UTF8, "application/json");

            var attachResponse = await _httpClient.SendAsync(attachRequest);
            var attachBody = await attachResponse.Content.ReadAsStringAsync();

            if (!attachResponse.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo attach error: {Code} - {Body}", attachResponse.StatusCode, attachBody);
                return new PayMongoSourceResult { Success = false, ErrorMessage = ParseErrorMessage(attachBody) ?? "Failed to attach payment method" };
            }

            var attachResult = JsonSerializer.Deserialize<JsonElement>(attachBody);
            var attachAttrs = attachResult.GetProperty("data").GetProperty("attributes");
            var status = attachAttrs.GetProperty("status").GetString()!;

            string? redirectUrl = null;
            if (status == "awaiting_next_action" && attachAttrs.TryGetProperty("next_action", out var nextAction))
            {
                if (nextAction.TryGetProperty("redirect", out var redirect))
                    redirectUrl = redirect.GetProperty("url").GetString();
            }

            // If already succeeded (e.g. card with no 3DS), no redirect needed
            if (status == "succeeded")
            {
                return new PayMongoSourceResult
                {
                    Success = true,
                    SourceId = intentId,
                    CheckoutUrl = null,
                    Status = "succeeded",
                    Type = type
                };
            }

            if (string.IsNullOrEmpty(redirectUrl))
            {
                return new PayMongoSourceResult { Success = false, ErrorMessage = "No redirect URL returned. Payment may not be supported for this method." };
            }

            _logger.LogInformation("PayMongo PI created: {IntentId}, method: {Type}, status: {Status}", intentId, type, status);

            return new PayMongoSourceResult
            {
                Success = true,
                SourceId = intentId,
                CheckoutUrl = redirectUrl,
                Status = status,
                Type = type
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error connecting to PayMongo");
            return new PayMongoSourceResult { Success = false, ErrorMessage = "Unable to connect to PayMongo. Please check your internet connection." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PayMongo payment intent");
            return new PayMongoSourceResult { Success = false, ErrorMessage = $"Payment processing error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Get the status of a Payment Intent
    /// </summary>
    public async Task<PayMongoSourceStatus> GetPaymentIntentStatus(string intentId)
    {
        if (!IsConfigured)
            return new PayMongoSourceStatus { Status = "error", ErrorMessage = "PayMongo not configured" };

        try
        {
            var request = CreateRequest(HttpMethod.Get, $"{BaseUrl}/payment_intents/{intentId}");
            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo PI status error: {Code}", response.StatusCode);
                return new PayMongoSourceStatus { Status = "error" };
            }

            var result = JsonSerializer.Deserialize<JsonElement>(body);
            var attrs = result.GetProperty("data").GetProperty("attributes");

            string? paymentId = null;
            if (attrs.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Array && payments.GetArrayLength() > 0)
            {
                paymentId = payments[0].GetProperty("id").GetString();
            }

            return new PayMongoSourceStatus
            {
                Status = attrs.GetProperty("status").GetString() ?? "pending",
                Amount = attrs.GetProperty("amount").GetInt64() / 100m,
                Type = paymentId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment intent status");
            return new PayMongoSourceStatus { Status = "error", ErrorMessage = ex.Message };
        }
    }

    private string? ParseErrorMessage(string responseBody)
    {
        try
        {
            var errorJson = JsonSerializer.Deserialize<JsonElement>(responseBody);
            if (errorJson.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var firstErr = errors[0];
                if (firstErr.TryGetProperty("detail", out var detail))
                    return detail.GetString();
            }
        }
        catch { }
        return null;
    }
}

// ========== DTO Classes ==========

public class PayMongoSourceResult
{
    public bool Success { get; set; }
    public string? SourceId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PayMongoSourceStatus
{
    public string Status { get; set; } = "pending";
    public string? Type { get; set; }
    public decimal Amount { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PayMongoPaymentResult
{
    public bool Success { get; set; }
    public string? PaymentId { get; set; }
    public string? Status { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal Amount { get; set; }
    public string? ErrorMessage { get; set; }
}

// Keep for backward compatibility
public class PayMongoCheckoutResult
{
    public bool Success { get; set; }
    public string? CheckoutSessionId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PayMongoPaymentStatus
{
    public string Status { get; set; } = "pending";
    public string? PaymentId { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? PaymentMethod { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PayMongoPaymentDetails
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PHP";
    public string Status { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
}

/// <summary>
/// Card details for card payment method
/// </summary>
public class CardDetails
{
    public string CardNumber { get; set; } = string.Empty;
    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }
    public string Cvc { get; set; } = string.Empty;
}
