namespace Conductor.Interfaces;

public interface IAuthorizedRequest
{
	IEnumerable<string> GetRequiredPermissions();
}