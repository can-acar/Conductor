using IsolationLevel = System.Data.IsolationLevel;

namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class TransactionAttribute : Attribute
{
	public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;
	public int TimeoutSeconds { get; set; } = 30;
	public bool RequiresNew { get; set; } = false;
	public string? ConnectionStringName { get; set; }
	public int Priority { get; set; } = 100;

	public TransactionAttribute()
	{
	}

	public TransactionAttribute(IsolationLevel isolationLevel)
	{
		IsolationLevel = isolationLevel;
	}

	public TransactionAttribute(IsolationLevel isolationLevel, int timeoutSeconds)
	{
		IsolationLevel = isolationLevel;
		TimeoutSeconds = timeoutSeconds;
	}
}