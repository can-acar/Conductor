using Conductor.Extensions;
using ExampleWebApplication.Handlers;
using ExampleWebApplication.Module;

namespace ExampleWebApplication.Configuration;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Register database (example)
        services.AddSingleton<ProductDb>();
            
        // Register Conductor with all modules
        services.AddConductor(options =>
        {
            options.HandlerAssemblies.Add(typeof(ProductListQueries).Assembly);
            options.EnableCaching = true;
            options.EnablePipelining = true;
            options.DefaultCacheExpiration = TimeSpan.FromMinutes(5);
        });

        // Register specific handlers
        services.AddScoped<ProductListQueries>();
        services.AddScoped<ProductEventHandlers>();
        services.AddScoped<ProductPipelineSteps>();

        // Add controllers
        services.AddControllers();
        services.AddLogging();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IServiceProvider serviceProvider)
    {
        // Initialize Conductor
        Conductor.Extensions.Conductor.Init(serviceProvider);

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}

// Alternative static initialization for simpler setup