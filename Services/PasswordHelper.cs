using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CKNDocument.Services;

/// <summary>
/// Password helper service for hashing and validation
/// Supports multiple hash formats for legacy compatibility
/// </summary>
public static class PasswordHelper
{
    private const int SaltSize = 16; // 128 bits
    private const int KeySize = 32; // 256 bits
    private const int Iterations = 100000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Hash a password using PBKDF2
    /// </summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            Algorithm,
            KeySize);

        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verify a password against a hash
    /// Supports multiple formats: PBKDF2 (salt.hash), BCrypt, SHA256, and legacy plain text
    /// </summary>
    public static bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
            return false;

        try
        {
            // Format 1: PBKDF2 format (salt.hash) - our new format
            if (passwordHash.Contains('.'))
            {
                var parts = passwordHash.Split('.');
                if (parts.Length == 2)
                {
                    try
                    {
                        var salt = Convert.FromBase64String(parts[0]);
                        var hash = Convert.FromBase64String(parts[1]);

                        var inputHash = Rfc2898DeriveBytes.Pbkdf2(
                            Encoding.UTF8.GetBytes(password),
                            salt,
                            Iterations,
                            Algorithm,
                            KeySize);

                        return CryptographicOperations.FixedTimeEquals(inputHash, hash);
                    }
                    catch
                    {
                        // Not a valid PBKDF2 hash, try other formats
                    }
                }
            }

            // Format 2: BCrypt format (starts with $2a$, $2b$, or $2y$)
            if (passwordHash.StartsWith("$2"))
            {
                // BCrypt verification would go here if needed
                // For now, we'll skip BCrypt support
                return false;
            }

            // Format 3: SHA256 hex format (64 characters, all hex)
            if (passwordHash.Length == 64 && IsHexString(passwordHash))
            {
                using var sha256 = SHA256.Create();
                var inputHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                var inputHashHex = Convert.ToHexString(inputHash).ToLower();
                return string.Equals(inputHashHex, passwordHash.ToLower(), StringComparison.OrdinalIgnoreCase);
            }

            // Format 4: Base64 encoded hash (various lengths - SHA256, SHA384, SHA512, etc.)
            // Try to decode as Base64 and compare
            if (IsBase64String(passwordHash))
            {
                try
                {
                    var storedHashBytes = Convert.FromBase64String(passwordHash);
                    
                    // Try SHA256 (32 bytes)
                    if (storedHashBytes.Length == 32)
                    {
                        using var sha256 = SHA256.Create();
                        var inputHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                        if (CryptographicOperations.FixedTimeEquals(inputHash, storedHashBytes))
                            return true;
                    }
                    
                    // Try SHA384 (48 bytes)
                    if (storedHashBytes.Length == 48)
                    {
                        using var sha384 = SHA384.Create();
                        var inputHash = sha384.ComputeHash(Encoding.UTF8.GetBytes(password));
                        if (CryptographicOperations.FixedTimeEquals(inputHash, storedHashBytes))
                            return true;
                    }
                    
                    // Try SHA512 (64 bytes)
                    if (storedHashBytes.Length == 64)
                    {
                        using var sha512 = SHA512.Create();
                        var inputHash = sha512.ComputeHash(Encoding.UTF8.GetBytes(password));
                        if (CryptographicOperations.FixedTimeEquals(inputHash, storedHashBytes))
                            return true;
                    }
                }
                catch
                {
                    // Not a valid Base64 hash, try other formats
                }
            }

            // Format 5: Plain text comparison (DEVELOPMENT ONLY - remove in production!)
            // This allows login with existing passwords that weren't hashed
            if (password == passwordHash)
            {
                return true;
            }

            // Format 6: Case-insensitive plain text (legacy systems)
            if (string.Equals(password, passwordHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Check if a string is a valid hexadecimal string
    /// </summary>
    private static bool IsHexString(string value)
    {
        return value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }

    /// <summary>
    /// Check if a string is a valid Base64 string
    /// </summary>
    private static bool IsBase64String(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 4)
            return false;

        // Must be multiple of 4 or have valid padding
        if (value.Length % 4 != 0)
            return false;

        // Check for valid Base64 characters
        foreach (var c in value)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || 
                  (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '='))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Validate password strength
    /// Requirements: 12+ chars, uppercase, lowercase, number, special character
    /// </summary>
    public static PasswordValidationResult ValidatePassword(string password)
    {
        var result = new PasswordValidationResult();

        if (string.IsNullOrEmpty(password))
        {
            result.Errors.Add("Password is required");
            return result;
        }

        if (password.Length < 12)
        {
            result.Errors.Add("Password must be at least 12 characters long");
        }

        if (!Regex.IsMatch(password, @"[A-Z]"))
        {
            result.Errors.Add("Password must contain at least one uppercase letter");
        }

        if (!Regex.IsMatch(password, @"[a-z]"))
        {
            result.Errors.Add("Password must contain at least one lowercase letter");
        }

        if (!Regex.IsMatch(password, @"[0-9]"))
        {
            result.Errors.Add("Password must contain at least one number");
        }

        if (!Regex.IsMatch(password, @"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]"))
        {
            result.Errors.Add("Password must contain at least one special character (!@#$%^&*()_+-=[]{}|;':\"\\,.<>/?)");
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }
}

/// <summary>
/// Password validation result
/// </summary>
public class PasswordValidationResult
{
    public bool IsValid { get; set; } = false;
    public List<string> Errors { get; set; } = new();
}
