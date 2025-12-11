using Conductor.Attributes;
using Conductor.Interfaces;

namespace Conductor.Core;

public abstract class AuthorizedAuditableRequest : BaseRequest, IAuthorizedRequest, IAuditableRequest
{
	public virtual IEnumerable<string> GetRequiredPermissions()
	{
		var attribute = GetType().GetCustomAttributes(typeof(AuthorizeAttribute), true)
								 .FirstOrDefault() as AuthorizeAttribute;
		return attribute?.Permissions ?? Enumerable.Empty<string>();
	}

	public virtual string GetAuditDetails()
	{
		var attribute = GetType().GetCustomAttributes(typeof(AuditableAttribute), true)
								 .FirstOrDefault() as AuditableAttribute;
		if (attribute?.LogRequestData == true)
		{
			return System.Text.Json.JsonSerializer.Serialize(this);
		}
		var uuid = Guid.NewGuid().ToString("N").Substring(0, 8);
		return $"Request: {GetType().Name}, Id: {uuid}";
	}
}