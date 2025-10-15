using System.Reflection;
using Conductor.Extensions;
using Conductor.Interfaces;
using Conductor.Modules.Cache;
using Conductor.Modules.Pipeline;
using Conductor.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Conductor.Core;

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
        services.AddHttpContextAccessor();
        services.AddSingleton<ICorrelationIdHelper, CorrelationIdHelper>();
        services.AddLogging();
    }

    public static void RegisterCacheModule(IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<ICacheModule, MemoryCacheModule>();
    }

    public static void RegisterPipelineModule(IServiceCollection services)
    {
        services.AddSingleton<IPipelineModule, PipelineModule>();
        services.AddConductorPipeline();
 
    }

    public static IConductor GetConductor()
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Conductor not initialized. Call Conductor.Init() first.");

        return _serviceProvider.GetRequiredService<IConductor>();
    }
}