namespace Conductor.Core;

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