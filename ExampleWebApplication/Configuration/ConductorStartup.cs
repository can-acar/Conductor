namespace ExampleWebApplication.Configuration;

public static class ConductorStartup
{
    public static void ConfigureConductor(IServiceCollection services)
    {
        Conductor.Core.Conductor.RegisterDependencyInjection(services);
        Conductor.Core.Conductor.RegisterCacheModule(services);
        Conductor.Core.Conductor.RegisterPipelineModule(services);
    }
}