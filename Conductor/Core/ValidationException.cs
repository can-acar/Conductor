namespace Conductor.Core;

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