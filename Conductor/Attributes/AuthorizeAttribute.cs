namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class AuthorizeAttribute : Attribute
{
	public string[] Permissions { get; set; } = Array.Empty<string>();

	public AuthorizeAttribute(params string[] permissions)
	{
		Permissions = permissions;
	}
}