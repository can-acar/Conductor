namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class TransactionalAttribute : Attribute
{
	public bool RequireTransaction { get; set; } = true;
}