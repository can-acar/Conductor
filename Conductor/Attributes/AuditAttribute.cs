using Conductor.Enums;

namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class AuditAttribute : Attribute
{
    public bool LogRequest { get; set; } = true;
    public bool LogResponse { get; set; } = true;
    public bool LogExecutionTime { get; set; } = true;
    public bool LogParameters { get; set; } = true;
    public string? Category { get; set; }
    public AuditLevel Level { get; set; } = AuditLevel.Information;
    public bool IncludeSensitiveData { get; set; } = false;
    public int Priority { get; set; } = 50;
    public string[]? ExcludeProperties { get; set; }

    public AuditAttribute()
    {
    }

    public AuditAttribute(AuditLevel level)
    {
        Level = level;
    }

    public AuditAttribute(string category, AuditLevel level = AuditLevel.Information)
    {
        Category = category;
        Level = level;
    }
}