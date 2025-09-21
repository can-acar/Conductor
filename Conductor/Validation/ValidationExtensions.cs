using Conductor.Attributes;

namespace Conductor.Validation;

public static class ValidationExtensions
{
    /// <summary>
    /// Extension method to add email validation
    /// </summary>
    public static IRuleBuilder<T, string> EmailAddress<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder.Must(BeValidEmail)
            .WithMessage("'{PropertyName}' is not a valid email address.")
            .WithErrorCode("INVALID_EMAIL");
    }

    /// <summary>
    /// Extension method to add custom regex validation
    /// </summary>
    public static IRuleBuilder<T, string> Matches<T>(this IRuleBuilder<T, string> ruleBuilder, string pattern)
    {
        return ruleBuilder.Must(value => string.IsNullOrEmpty(value) || System.Text.RegularExpressions.Regex.IsMatch(value, pattern))
            .WithMessage($"'{{PropertyName}}' does not match the required pattern.")
            .WithErrorCode("REGEX_MISMATCH");
    }

    /// <summary>
    /// Extension method to add phone number validation
    /// </summary>
    public static IRuleBuilder<T, string> PhoneNumber<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder.Must(BeValidPhoneNumber)
            .WithMessage("'{PropertyName}' is not a valid phone number.")
            .WithErrorCode("INVALID_PHONE");
    }

    /// <summary>
    /// Extension method to add URL validation
    /// </summary>
    public static IRuleBuilder<T, string> Url<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder.Must(BeValidUrl)
            .WithMessage("'{PropertyName}' is not a valid URL.")
            .WithErrorCode("INVALID_URL");
    }

    /// <summary>
    /// Extension method to validate that a value is in a collection
    /// </summary>
    public static IRuleBuilder<T, TProperty> IsInEnum<T, TProperty>(this IRuleBuilder<T, TProperty> ruleBuilder)
        where TProperty : struct, Enum
    {
        return ruleBuilder.Must(value => Enum.IsDefined(typeof(TProperty), value))
            .WithMessage("'{PropertyName}' has an invalid value.")
            .WithErrorCode("INVALID_ENUM");
    }

    /// <summary>
    /// Extension method to validate that a string value is in a predefined collection
    /// </summary>
    public static IRuleBuilder<T, string> IsIn<T>(this IRuleBuilder<T, string> ruleBuilder, params string[] validValues)
    {
        return ruleBuilder.Must(value => string.IsNullOrEmpty(value) || validValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"'{{PropertyName}}' must be one of: {string.Join(", ", validValues)}")
            .WithErrorCode("INVALID_CHOICE");
    }

    private static bool BeValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return true; // Let NotEmpty handle empty validation

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private static bool BeValidPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return true; // Let NotEmpty handle empty validation

        // Simple phone number validation - can be enhanced based on requirements
        var digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());
        return digitsOnly.Length >= 10 && digitsOnly.Length <= 15;
    }

    private static bool BeValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true; // Let NotEmpty handle empty validation

        return Uri.TryCreate(url, UriKind.Absolute, out var result)
               && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
    }
}