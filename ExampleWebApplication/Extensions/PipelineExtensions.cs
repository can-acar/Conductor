using Conductor.Interfaces;
using ExampleWebApplication.Behaviors;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class PipelineExtensions
{
    // This class is intentionally left empty.
    // It can be used in the future to add extension methods for pipeline steps if needed.

    
    public static IServiceCollection AddAuthorizationBehavior(this IServiceCollection services)
    {
        services.TryAddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        return services;
    }
}