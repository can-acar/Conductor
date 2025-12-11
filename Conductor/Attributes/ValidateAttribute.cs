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