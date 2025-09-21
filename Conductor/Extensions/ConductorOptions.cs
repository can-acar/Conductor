using System.Reflection;

namespace Conductor.Extensions;

public class ConductorOptions
{
    public List<Assembly> HandlerAssemblies { get; set; } = new();
    public bool EnableCaching { get; set; } = true;
    public bool EnablePipelining { get; set; } = true;
    public TimeSpan DefaultCacheExpiration { get; set; } = TimeSpan.FromMinutes(5);
}