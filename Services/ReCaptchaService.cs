using System.Text.Json;
using System.Text.Json.Serialization;

namespace CKNDocument.Services;

/// <summary>
/// Service to verify Google reCAPTCHA v3 tokens on the server side.
/// reCAPTCHA v3 returns a score (0.0 - 1.0) where 1.0 is very likely a good interaction.
/// </summary>
public class ReCaptchaService
{
    private readonly HttpClient _httpClient;
    private readonly string _secretKey;
    private readonly ILogger<ReCaptchaService> _logger;

    /// <summary>
    /// Minimum score threshold. Requests scoring below this are treated as bots.
    /// 0.5 is Google's recommended default.
    /// </summary>
    private const float MinimumScore = 0.5f;

    public ReCaptchaService(HttpClient httpClient, IConfiguration configuration, ILogger<ReCaptchaService> logger)
    {
        _httpClient = httpClient;
        _secretKey = configuration["GoogleReCaptcha:SecretKey"]
            ?? throw new InvalidOperationException("GoogleReCaptcha:SecretKey is not configured.");
        _logger = logger;
    }

    /// <summary>
    /// Verifies a reCAPTCHA v3 response token with Google's API.
    /// </summary>
    /// <param name="recaptchaResponse">The reCAPTCHA token from the client.</param>
    /// <param name="expectedAction">The expected action name (e.g. "login", "register").</param>
    /// <returns>True if the captcha is valid and score is above threshold; otherwise false.</returns>
    public async Task<bool> VerifyAsync(string? recaptchaResponse, string? expectedAction = null)
    {
        if (string.IsNullOrWhiteSpace(recaptchaResponse))
        {
            _logger.LogWarning("reCAPTCHA response token is empty or null.");
            return false;
        }

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "secret", _secretKey },
                { "response", recaptchaResponse }
            });

            var response = await _httpClient.PostAsync("https://www.google.com/recaptcha/api/siteverify", content);
            var json = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("reCAPTCHA verify response: {Json}", json);

            var result = JsonSerializer.Deserialize<ReCaptchaVerificationResult>(json);

            if (result == null)
            {
                _logger.LogWarning("reCAPTCHA verification returned null result.");
                return false;
            }

            if (!result.Success)
            {
                _logger.LogWarning("reCAPTCHA verification failed. Error codes: {Errors}",
                    result.ErrorCodes != null ? string.Join(", ", result.ErrorCodes) : "none");
                return false;
            }

            // Check the score (v3 specific)
            if (result.Score < MinimumScore)
            {
                _logger.LogWarning("reCAPTCHA score {Score} is below minimum threshold {MinScore} for action '{Action}'.",
                    result.Score, MinimumScore, result.Action);
                return false;
            }

            // Optionally verify the action matches
            if (!string.IsNullOrEmpty(expectedAction) &&
                !string.Equals(result.Action, expectedAction, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("reCAPTCHA action mismatch. Expected '{Expected}', got '{Actual}'.",
                    expectedAction, result.Action);
                return false;
            }

            _logger.LogInformation("reCAPTCHA v3 passed. Score: {Score}, Action: {Action}", result.Score, result.Action);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying reCAPTCHA token.");
            return false;
        }
    }
}

/// <summary>
/// Represents the JSON response from Google's reCAPTCHA v3 siteverify endpoint.
/// </summary>
public class ReCaptchaVerificationResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("score")]
    public float Score { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("challenge_ts")]
    public string? ChallengeTimestamp { get; set; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; set; }
}
