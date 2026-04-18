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
    private string? _secretKey;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ReCaptchaService> _logger;
    private readonly float _minimumScore;

    /// <summary>
    /// Minimum score threshold for v3 responses. Defaults to 0.3 to reduce false negatives.
    /// Can be overridden by GoogleReCaptcha:MinimumScore.
    /// </summary>
    private const float DefaultMinimumScore = 0.3f;

    public ReCaptchaService(
        HttpClient httpClient,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ReCaptchaService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _secretKey = ResolveSecretKey();

        _httpContextAccessor = httpContextAccessor;
        _minimumScore = Math.Clamp(
            configuration.GetValue<float?>("GoogleReCaptcha:MinimumScore") ?? DefaultMinimumScore,
            0f,
            1f);
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_secretKey))
        {
            _logger.LogError("GoogleReCaptcha:SecretKey is not configured. Set GoogleReCaptcha__SecretKey in .env or OS environment variables.");
        }
    }

    private string? ResolveSecretKey()
    {
        var configuredSecret = NormalizeValue(_configuration["GoogleReCaptcha:SecretKey"]);
        if (!string.IsNullOrWhiteSpace(configuredSecret))
            return configuredSecret;

        var envSecret = NormalizeValue(Environment.GetEnvironmentVariable("GoogleReCaptcha__SecretKey"));
        if (!string.IsNullOrWhiteSpace(envSecret))
            return envSecret;

        return NormalizeValue(Environment.GetEnvironmentVariable("GOOGLE_RECAPTCHA_SECRET_KEY"));
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().Trim('"');
    }

    /// <summary>
    /// Verifies a reCAPTCHA v3 response token with Google's API.
    /// </summary>
    /// <param name="recaptchaResponse">The reCAPTCHA token from the client.</param>
    /// <param name="expectedAction">The expected action name (e.g. "login", "register").</param>
    /// <returns>True if the captcha is valid and score is above threshold; otherwise false.</returns>
    public async Task<bool> VerifyAsync(string? recaptchaResponse, string? expectedAction = null)
    {
        _secretKey ??= ResolveSecretKey();
        if (string.IsNullOrWhiteSpace(_secretKey))
        {
            _logger.LogError("reCAPTCHA verification skipped because secret key is missing.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(recaptchaResponse))
        {
            _logger.LogWarning("reCAPTCHA response token is empty or null.");
            return false;
        }

        try
        {
            var form = new Dictionary<string, string>
            {
                { "secret", _secretKey },
                { "response", recaptchaResponse }
            };

            var remoteIp = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();
            if (!string.IsNullOrWhiteSpace(remoteIp))
            {
                form["remoteip"] = remoteIp;
            }

            var content = new FormUrlEncodedContent(form);

            var response = await _httpClient.PostAsync("https://www.google.com/recaptcha/api/siteverify", content);
            var json = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("reCAPTCHA verify response: {Json}", json);

            var result = JsonSerializer.Deserialize<ReCaptchaVerificationResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

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

            // v3 returns score/action. v2 may omit both and still be valid (success=true).
            var isV3Response = result.Score > 0 || !string.IsNullOrWhiteSpace(result.Action);

            if (isV3Response && result.Score < _minimumScore)
            {
                _logger.LogWarning("reCAPTCHA score {Score} is below minimum threshold {MinScore} for action '{Action}'.",
                    result.Score, _minimumScore, result.Action);
                return false;
            }

            // Validate action when Google sends one; don't fail hard when action is absent.
            if (!string.IsNullOrEmpty(expectedAction) &&
                !string.IsNullOrWhiteSpace(result.Action) &&
                !string.Equals(result.Action, expectedAction, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("reCAPTCHA action mismatch. Expected '{Expected}', got '{Actual}'.",
                    expectedAction, result.Action);
                return false;
            }

            if (!isV3Response)
            {
                _logger.LogInformation("reCAPTCHA validated without v3 score/action. Accepted based on success response.");
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
