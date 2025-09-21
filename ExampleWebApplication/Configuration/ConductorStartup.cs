namespace ExampleWebApplication.Configuration;

public static class ConductorStartup
{
    public static void ConfigureConductor(IServiceCollection services)
    {
        Conductor.Extensions.Conductor.RegisterDependencyInjection(services);
        Conductor.Extensions.Conductor.RegisterCacheModule(services);
        Conductor.Extensions.Conductor.RegisterPipelineModule(services);
    }
}