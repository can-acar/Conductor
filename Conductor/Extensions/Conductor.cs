using System.Reflection;
using Conductor.Core;
using Conductor.Modules.Cache;
using Conductor.Modules.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Conductor.Extensions;

public static class Conductor
{
    private static IServiceProvider? _serviceProvider;

    public static void Init(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public static void RegisterDependencyInjection(IServiceCollection services)
    {
        services.AddConductor(options =>
        {
            options.HandlerAssemblies.Add(Assembly.GetExecutingAssembly());
            options.HandlerAssemblies.Add(Assembly.GetCallingAssembly());
        });
    }

    public static void RegisterCacheModule(IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<ICacheModule, MemoryCacheModule>();
    }

    public static void RegisterPipelineModule(IServiceCollection services)
    {
        services.AddSingleton<IPipelineModule, PipelineModule>();
    }

    public static IConductor GetConductor()
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Conductor not initialized. Call Conductor.Init() first.");

        return _serviceProvider.GetRequiredService<IConductor>();
    }
}