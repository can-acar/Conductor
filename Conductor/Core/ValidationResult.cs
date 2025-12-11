namespace Conductor.Core;

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