using System.Reflection;
using Conductor.Attributes;
using Conductor.Core;
using Conductor.Modules.Cache;
using Conductor.Modules.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace Conductor.Extensions;

public static class ConductorExtensions
{
    public static IServiceCollection AddConductor(this IServiceCollection services,
        Action<ConductorOptions>? configure = null)
    {
        var options = new ConductorOptions();
        configure?.Invoke(options);

        // Register core services
        services.AddSingleton<IConductor, ConductorService>();

        // Register cache module
        services.AddMemoryCache();
        services.AddSingleton<ICacheModule, MemoryCacheModule>();

        // Register pipeline module
        services.AddSingleton<IPipelineModule, PipelineModule>();

        // Register audit logger
        services.AddSingleton<IAuditLogger, DefaultAuditLogger>();

        // Auto-register handlers from specified assemblies
        foreach (var assembly in options.HandlerAssemblies)
        {
            RegisterHandlersFromAssembly(services, assembly);
            RegisterValidatorsFromAssembly(services, assembly);
        }

        return services;
    }

    private static void RegisterHandlersFromAssembly(IServiceCollection services, Assembly assembly)
    {
        var types = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetMethods().Any(m =>
                m.GetCustomAttribute<HandleAttribute>() != null ||
                m.GetCustomAttribute<SagaAttribute>() != null ||
                m.GetCustomAttribute<PipelineAttribute>() != null));

        foreach (var type in types)
        {
            services.AddScoped(type);
        }
    }

    private static void RegisterValidatorsFromAssembly(IServiceCollection services, Assembly assembly)
    {
        var validatorTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            .ToList();

        Console.WriteLine($"[Conductor] Found {validatorTypes.Count} validator(s) in assembly {assembly.GetName().Name}");

        foreach (var validatorType in validatorTypes)
        {
            // Find all IValidator<T> interfaces this type implements
            var validatorInterfaces = validatorType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
                .ToList();

            foreach (var validatorInterface in validatorInterfaces)
            {
                var validatedType = validatorInterface.GetGenericArguments()[0];
                Console.WriteLine($"[Conductor] Registering validator: {validatorType.Name} for type: {validatedType.Name}");

                // Register both the interface and the concrete type
                services.AddScoped(validatorInterface, validatorType);

                // Only register concrete type once to avoid duplicates
                if (!services.Any(s => s.ServiceType == validatorType))
                {
                    services.AddScoped(validatorType);
                }
            }
        }
    }

    private static void RegisterValidatorForType(IServiceCollection services, Type validatorType, Type validatedType)
    {
        var validatorInterfaceType = typeof(IValidator<>).MakeGenericType(validatedType);
        services.AddScoped(validatorInterfaceType, validatorType);
        services.AddScoped(validatorType);
    }
}

// Static Conductor class for easy initialization