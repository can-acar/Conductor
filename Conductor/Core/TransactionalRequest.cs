using Conductor.Attributes;
using Conductor.Interfaces;

namespace Conductor.Core;

public abstract class TransactionalRequest : BaseRequest, ITransactionalRequest
{
	public virtual bool RequiresTransaction
	{
		get
		{
			var attribute = GetType().GetCustomAttributes(typeof(TransactionalAttribute), true)
									 .FirstOrDefault() as TransactionalAttribute;
			return attribute?.RequireTransaction ?? true;
		}
	}
}