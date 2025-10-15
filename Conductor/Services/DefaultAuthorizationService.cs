using Conductor.Interfaces;
using Microsoft.Extensions.Logging;

namespace Conductor.Services;

public class DefaultAuthorizationService : IAuthorizationService
{
    private readonly Microsoft.Extensions.Logging.ILogger<DefaultAuthorizationService> _logger;

    public DefaultAuthorizationService(Microsoft.Extensions.Logging.ILogger<DefaultAuthorizationService> logger)
    {
        _logger = logger;
    }

    public Task<bool> IsAuthorizedAsync(string userId, IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        // Default implementation - always authorize
        // Override this with your actual authorization logic
        _logger.LogDebug("Authorization check for user {UserId} with permissions {Permissions}",
            userId, string.Join(", ", permissions));

        return Task.FromResult(true);
    }
}