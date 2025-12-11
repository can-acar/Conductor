using Conductor.Attributes;
using Conductor.Interfaces;

namespace Conductor.Core;

public abstract class AuditableRequest : BaseRequest, IAuditableRequest
{
	public virtual string GetAuditDetails()
	{
		var attribute = GetType().GetCustomAttributes(typeof(AuditableAttribute), true)
								 .FirstOrDefault() as AuditableAttribute;
		var uuid = Guid.NewGuid().ToString("N").Substring(0, 8);
		if (attribute?.LogRequestData == true)
		{
			return System.Text.Json.JsonSerializer.Serialize(this);
		}
		return $"Request: {GetType().Name}, Id: {uuid}";
	}
}