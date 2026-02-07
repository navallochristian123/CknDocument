using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CKNDocument.Services;

/// <summary>
/// Service for PayMongo payment gateway integration
/// Uses environment variable PAYMONGO_SECRET_KEY for API authentication
/// </summary>
public class PayMongoService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PayMongoService> _logger;
    private readonly string _secretKey;
    private const string BaseUrl = "https://api.paymongo.com/v1";

    public PayMongoService(HttpClient httpClient, ILogger<PayMongoService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _secretKey = Environment.GetEnvironmentVariable("PAYMONGO_SECRET_KEY")
            ?? throw new InvalidOperationException("PAYMONGO_SECRET_KEY environment variable is not set.");

        var authBytes = Encoding.UTF8.GetBytes($"{_secretKey}:");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Create a PayMongo Checkout Session for payment
    /// </summary>
    public async Task<PayMongoCheckoutResult> CreateCheckoutSession(
        decimal amount,
        string description,
        string invoiceNumber,
        string firmName,
        string successUrl,
        string cancelUrl)
    {
        try
        {
            // PayMongo expects amount in centavos (smallest currency unit)
            var amountInCentavos = (int)(amount * 100);

            var payload = new
            {
                data = new
                {
                    attributes = new
                    {
                        send_email_receipt = true,
                        show_description = true,
                        show_line_items = true,
                        description = description,
                        line_items = new[]
                        {
                            new
                            {
                                currency = "PHP",
                                amount = amountInCentavos,
                                description = description,
                                name = $"CKN Subscription - {firmName}",
                                quantity = 1
                            }
                        },
                        payment_method_types = new[] { "gcash", "grab_pay", "card", "paymaya" },
                        success_url = successUrl,
                        cancel_url = cancelUrl,
                        reference_number = invoiceNumber,
                        metadata = new
                        {
                            invoice_number = invoiceNumber,
                            firm_name = firmName
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{BaseUrl}/checkout_sessions", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                return new PayMongoCheckoutResult
                {
                    Success = false,
                    ErrorMessage = $"PayMongo API error: {response.StatusCode}"
                };
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var data = result.GetProperty("data");
            var attributes = data.GetProperty("attributes");

            return new PayMongoCheckoutResult
            {
                Success = true,
                CheckoutSessionId = data.GetProperty("id").GetString()!,
                CheckoutUrl = attributes.GetProperty("checkout_url").GetString()!,
                PaymentIntentId = attributes.TryGetProperty("payment_intent", out var pi) && pi.ValueKind != JsonValueKind.Null
                    ? pi.GetProperty("id").GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PayMongo checkout session");
            return new PayMongoCheckoutResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Retrieve a checkout session to check payment status
    /// </summary>
    public async Task<PayMongoPaymentStatus> GetCheckoutSessionStatus(string checkoutSessionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/checkout_sessions/{checkoutSessionId}");
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                return new PayMongoPaymentStatus { Status = "error" };
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var attributes = result.GetProperty("data").GetProperty("attributes");

            var status = "pending";
            string? paymentId = null;
            string? paymentMethod = null;

            // Check payments array
            if (attributes.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Array)
            {
                foreach (var payment in payments.EnumerateArray())
                {
                    var payAttrs = payment.GetProperty("attributes");
                    var payStatus = payAttrs.GetProperty("status").GetString();
                    paymentId = payment.GetProperty("id").GetString();

                    if (payAttrs.TryGetProperty("source", out var source) && source.ValueKind != JsonValueKind.Null)
                    {
                        paymentMethod = source.TryGetProperty("type", out var type) ? type.GetString() : "card";
                    }

                    if (payStatus == "paid")
                    {
                        status = "paid";
                        break;
                    }
                    else if (payStatus == "failed")
                    {
                        status = "failed";
                    }
                }
            }

            // Fall back to checkout session status
            if (status == "pending")
            {
                var sessionStatus = attributes.GetProperty("status").GetString();
                if (sessionStatus == "expired") status = "expired";
            }

            var paymentIntentId = attributes.TryGetProperty("payment_intent", out var piElem) && piElem.ValueKind != JsonValueKind.Null
                ? (piElem.TryGetProperty("id", out var piId) ? piId.GetString() : null)
                : null;

            return new PayMongoPaymentStatus
            {
                Status = status,
                PaymentId = paymentId,
                PaymentIntentId = paymentIntentId,
                PaymentMethod = paymentMethod
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting PayMongo checkout session status");
            return new PayMongoPaymentStatus { Status = "error" };
        }
    }

    /// <summary>
    /// Retrieve payment details by ID
    /// </summary>
    public async Task<PayMongoPaymentDetails?> GetPaymentDetails(string paymentId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/payments/{paymentId}");
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return null;

            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var attrs = result.GetProperty("data").GetProperty("attributes");

            return new PayMongoPaymentDetails
            {
                Id = result.GetProperty("data").GetProperty("id").GetString()!,
                Amount = attrs.GetProperty("amount").GetInt64() / 100m,
                Currency = attrs.GetProperty("currency").GetString()!,
                Status = attrs.GetProperty("status").GetString()!,
                PaidAt = attrs.TryGetProperty("paid_at", out var paidAt) && paidAt.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(paidAt.GetInt64()).DateTime
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting PayMongo payment details");
            return null;
        }
    }
}

// DTO classes for PayMongo responses
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
}

public class PayMongoPaymentDetails
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PHP";
    public string Status { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
}
