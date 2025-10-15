using System.ComponentModel.DataAnnotations;

namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class ValidateAttribute : Attribute
{
    public bool ValidateRequest { get; set; } = true;
    public bool ValidateResponse { get; set; } = false;
    public bool ThrowOnValidationError { get; set; } = true;
    public string? ValidatorType { get; set; }
    public int Priority { get; set; } = 0;

    public ValidateAttribute()
    {
    }

    public ValidateAttribute(Type validatorType)
    {
        ValidatorType = validatorType.FullName;
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();

    public static ValidationResult Success() => new() { IsValid = true };

    public static ValidationResult Failure(params ValidationError[] errors) => new()
    {
        IsValid = false,
        Errors = errors.ToList()
    };
}

public class ValidationError
{
    public string PropertyName { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public object? AttemptedValue { get; set; }

    public ValidationError()
    {
    }

    public ValidationError(string propertyName, string errorMessage, string errorCode = "")
    {
        PropertyName = propertyName;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
    }
}

public class ValidationException : Exception
{
    public ValidationResult ValidationResult { get; }

    public ValidationException(ValidationResult validationResult)
        : base($"Validation failed with {validationResult.Errors.Count} error(s)")
    {
        ValidationResult = validationResult;
    }

    public ValidationException(string message, ValidationResult validationResult)
        : base(message)
    {
        ValidationResult = validationResult;
    }
}