using Conductor.Attributes;
using Conductor.Interfaces;

namespace Conductor.Core;

public abstract class AuthorizedRequest : BaseRequest, IAuthorizedRequest
{
	public virtual IEnumerable<string> GetRequiredPermissions()
	{
		var attribute = GetType().GetCustomAttributes(typeof(AuthorizeAttribute), true)
								 .FirstOrDefault() as AuthorizeAttribute;
		return attribute?.Permissions ?? Enumerable.Empty<string>();
	}
}