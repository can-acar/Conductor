namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class AuditableAttribute : Attribute
{
	public bool LogRequestData { get; set; } = false;
	public bool LogResponseData { get; set; } = false;
	public string? Category { get; set; }
}