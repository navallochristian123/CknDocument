using System.Net;
using System.Net.Mail;

namespace CKNDocument.Services;

/// <summary>
/// Sends emails using SMTP configuration from app configuration or environment variables.
/// </summary>
public class SmtpEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendLoginNotificationAsync(string recipientEmail, string recipientName, string role)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return false;
        }

        var host = Resolve("EmailSmtp:Host", "EmailSmtp__Host", "SMTP_HOST");
        var portValue = Resolve("EmailSmtp:Port", "EmailSmtp__Port", "SMTP_PORT");
        var sslValue = Resolve("EmailSmtp:EnableSsl", "EmailSmtp__EnableSsl", "SMTP_ENABLE_SSL");
        var username = Resolve("EmailSmtp:Username", "EmailSmtp__Username", "SMTP_USERNAME");
        var password = Resolve("EmailSmtp:Password", "EmailSmtp__Password", "SMTP_PASSWORD");
        var fromName = Resolve("EmailSmtp:FromName", "EmailSmtp__FromName", "SMTP_FROM_NAME") ?? "CKNDocument";

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(portValue) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            !int.TryParse(portValue, out var port))
        {
            _logger.LogWarning("SMTP is not fully configured. Skipping login notification email.");
            return false;
        }

        var enableSsl = true;
        if (!string.IsNullOrWhiteSpace(sslValue) && bool.TryParse(sslValue, out var parsedSsl))
        {
            enableSsl = parsedSsl;
        }

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
        var safeName = string.IsNullOrWhiteSpace(recipientName) ? "User" : recipientName;

        var subject = "New login detected";
        var body = $@"
            <div style='font-family: Arial, sans-serif; color: #1f2937;'>
                <h2 style='margin-bottom: 8px;'>Login Notification</h2>
                <p>Hello {WebUtility.HtmlEncode(safeName)},</p>
                <p>We detected a successful login to your account.</p>
                <ul>
                    <li><strong>Role:</strong> {WebUtility.HtmlEncode(role)}</li>
                    <li><strong>Time:</strong> {timestamp}</li>
                </ul>
                <p>If this was not you, please change your password immediately and contact your administrator.</p>
                <p style='margin-top: 18px;'>CKNDocument Security</p>
            </div>";

        try
        {
            using var smtpClient = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Credentials = new NetworkCredential(username, password),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            using var message = new MailMessage
            {
                From = new MailAddress(username, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            message.To.Add(recipientEmail);
            await smtpClient.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send login notification email to {RecipientEmail}", recipientEmail);
            return false;
        }
    }

    public async Task<bool> SendLoginOtpAsync(string recipientEmail, string recipientName, string otpCode, int expiryMinutes)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail) || string.IsNullOrWhiteSpace(otpCode))
        {
            return false;
        }

        var host = Resolve("EmailSmtp:Host", "EmailSmtp__Host", "SMTP_HOST");
        var portValue = Resolve("EmailSmtp:Port", "EmailSmtp__Port", "SMTP_PORT");
        var sslValue = Resolve("EmailSmtp:EnableSsl", "EmailSmtp__EnableSsl", "SMTP_ENABLE_SSL");
        var username = Resolve("EmailSmtp:Username", "EmailSmtp__Username", "SMTP_USERNAME");
        var password = Resolve("EmailSmtp:Password", "EmailSmtp__Password", "SMTP_PASSWORD");
        var fromName = Resolve("EmailSmtp:FromName", "EmailSmtp__FromName", "SMTP_FROM_NAME") ?? "CKNDocument";

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(portValue) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            !int.TryParse(portValue, out var port))
        {
            _logger.LogWarning("SMTP is not fully configured. Skipping OTP email.");
            return false;
        }

        var enableSsl = true;
        if (!string.IsNullOrWhiteSpace(sslValue) && bool.TryParse(sslValue, out var parsedSsl))
        {
            enableSsl = parsedSsl;
        }

        var safeName = string.IsNullOrWhiteSpace(recipientName) ? "User" : recipientName;
        var subject = "Your authentication code";
        var body = $@"
            <div style='font-family: Arial, sans-serif; color: #1f2937;'>
                <h2 style='margin-bottom: 8px;'>Two-step Verification</h2>
                <p>Hello {WebUtility.HtmlEncode(safeName)},</p>
                <p>Use the code below to complete your login:</p>
                <div style='font-size: 28px; letter-spacing: 4px; font-weight: bold; margin: 14px 0;'>
                    {WebUtility.HtmlEncode(otpCode)}
                </div>
                <p>This code expires in {expiryMinutes} minutes.</p>
                <p>If you did not request this login, contact your administrator immediately.</p>
            </div>";

        try
        {
            using var smtpClient = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Credentials = new NetworkCredential(username, password),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            using var message = new MailMessage
            {
                From = new MailAddress(username, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            message.To.Add(recipientEmail);
            await smtpClient.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send OTP email to {RecipientEmail}", recipientEmail);
            return false;
        }
    }

    public async Task<bool> SendPasswordResetOtpAsync(string recipientEmail, string recipientName, string otpCode, int expiryMinutes)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail) || string.IsNullOrWhiteSpace(otpCode))
        {
            return false;
        }

        var host = Resolve("EmailSmtp:Host", "EmailSmtp__Host", "SMTP_HOST");
        var portValue = Resolve("EmailSmtp:Port", "EmailSmtp__Port", "SMTP_PORT");
        var sslValue = Resolve("EmailSmtp:EnableSsl", "EmailSmtp__EnableSsl", "SMTP_ENABLE_SSL");
        var username = Resolve("EmailSmtp:Username", "EmailSmtp__Username", "SMTP_USERNAME");
        var password = Resolve("EmailSmtp:Password", "EmailSmtp__Password", "SMTP_PASSWORD");
        var fromName = Resolve("EmailSmtp:FromName", "EmailSmtp__FromName", "SMTP_FROM_NAME") ?? "CKNDocument";

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(portValue) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            !int.TryParse(portValue, out var port))
        {
            _logger.LogWarning("SMTP is not fully configured. Skipping password reset OTP email.");
            return false;
        }

        var enableSsl = true;
        if (!string.IsNullOrWhiteSpace(sslValue) && bool.TryParse(sslValue, out var parsedSsl))
        {
            enableSsl = parsedSsl;
        }

        var safeName = string.IsNullOrWhiteSpace(recipientName) ? "User" : recipientName;
        var subject = "Your password reset code";
        var body = $@"
            <div style='font-family: Arial, sans-serif; color: #1f2937;'>
                <h2 style='margin-bottom: 8px;'>Password Reset Verification</h2>
                <p>Hello {WebUtility.HtmlEncode(safeName)},</p>
                <p>Use the code below to continue resetting your password:</p>
                <div style='font-size: 28px; letter-spacing: 4px; font-weight: bold; margin: 14px 0;'>
                    {WebUtility.HtmlEncode(otpCode)}
                </div>
                <p>This code expires in {expiryMinutes} minutes.</p>
                <p>If you did not request this, you can ignore this email.</p>
            </div>";

        try
        {
            using var smtpClient = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Credentials = new NetworkCredential(username, password),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            using var message = new MailMessage
            {
                From = new MailAddress(username, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            message.To.Add(recipientEmail);
            await smtpClient.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send password reset OTP email to {RecipientEmail}", recipientEmail);
            return false;
        }
    }

    private string? Resolve(string configKey, string envKey1, string envKey2)
    {
        var value = _configuration[configKey];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim().Trim('"');
        }

        value = Environment.GetEnvironmentVariable(envKey1);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim().Trim('"');
        }

        value = Environment.GetEnvironmentVariable(envKey2);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim().Trim('"');
        }

        return null;
    }
}
