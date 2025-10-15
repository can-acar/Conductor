namespace Conductor.Interfaces;

public interface IAuthorizationService
{
    Task<bool> IsAuthorizedAsync(string userId, IEnumerable<string> permissions, CancellationToken cancellationToken = default);
}