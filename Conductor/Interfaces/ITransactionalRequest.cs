namespace Conductor.Interfaces;

public interface ITransactionalRequest
{
	bool RequiresTransaction { get; }
}