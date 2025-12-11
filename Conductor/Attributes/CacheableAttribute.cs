namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class CacheableAttribute : Attribute
{
	public string? CacheKey { get; set; }
	public int DurationSeconds { get; set; } = 300; // 5 minutes default
	public bool UseRequestData { get; set; } = true;
	public TimeSpan GetDuration() => TimeSpan.FromSeconds(DurationSeconds);
}