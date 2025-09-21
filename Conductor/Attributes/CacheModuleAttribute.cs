namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class CacheModuleAttribute : Attribute
{
    public int Duration { get; set; } = 300; // Default 5 minutes
    public string? CacheKey { get; set; }
    public bool SlidingExpiration { get; set; } = false;
    public string[]? Tags { get; set; }
}