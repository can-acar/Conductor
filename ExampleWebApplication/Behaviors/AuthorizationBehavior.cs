using Conductor.Core;
using Conductor.Interfaces;
using Conductor.Pipeline;
using Microsoft.AspNetCore.Http;

namespace ExampleWebApplication.Behaviors;

public class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : BaseRequest, IAuthorizedRequest
{
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<AuthorizationBehavior<TRequest, TResponse>> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthorizationBehavior(IAuthorizationService authorizationService, ILogger<AuthorizationBehavior<TRequest, TResponse>> logger, IHttpContextAccessor httpContextAccessor)
    {
        _authorizationService = authorizationService;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requiredPermissions = request.GetRequiredPermissions();

        if (requiredPermissions.Any())
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var userId = user?.Identity?.Name ?? user?.FindFirst("sub")?.Value ?? user?.FindFirst("uid")?.Value ?? "anonymous";

            var isAuthorized = await _authorizationService.IsAuthorizedAsync(userId, requiredPermissions, cancellationToken);

            if (!isAuthorized)
            {
                _logger.LogWarning("Authorization failed for user {UserId} on {RequestName}. Required permissions: {Permissions}", userId, typeof(TRequest).Name, string.Join(", ", requiredPermissions));

                throw new UnauthorizedAccessException($"Insufficient permissions for {typeof(TRequest).Name}");
            }

            _logger.LogDebug("Authorization successful for user {UserId} on {RequestName}", userId, typeof(TRequest).Name);
        }

        return await next();
    }
}