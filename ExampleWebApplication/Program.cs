using Conductor.Extensions;
using Conductor.Middleware;
using ExampleWebApplication;
using ExampleWebApplication.Configuration;
using ExampleWebApplication.Handlers;
using ExampleWebApplication.Module;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ProductDb>();
// Add services to the container.
builder.Services.AddConductor(options =>
{
    options.HandlerAssemblies.Add(typeof(ProductListQueries).Assembly);
    options.EnableCaching = true;
    options.EnablePipelining = true;
    options.DefaultCacheExpiration = TimeSpan.FromMinutes(5);
});

builder.Services.AddScoped<ProductListQueries>();
builder.Services.AddScoped<ProductEventHandlers>();
builder.Services.AddScoped<ProductPipelineSteps>();

// Validators are now auto-registered by Conductor framework



// Add controllers
builder.Services.AddControllers();
builder.Services.AddLogging();

// Configure OpenAPI/Swagger
builder.Services.AddOpenApi(options => { options.AddDocumentTransformer<ApiDocumentTransformer>(); });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Conductor Example API",
        Version = "v1",
        Description = "A sample API demonstrating the Conductor framework capabilities",
        Contact = new OpenApiContact
        {
            Name = "Example Contact",
            Email = "example@example.com"
        }
    });

    // Include XML comments for better documentation
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

ConductorStartup.ConfigureConductor(builder.Services);
var app = builder.Build();

// Initialize Conductor with service provider
Conductor.Core.Conductor.Init(app.Services);



// Configure the HTTP request pipeline.
// Add global exception handling first
app.UseGlobalExceptionHandling();

// Add response formatter middleware (after exception handling)
app.UseResponseFormatter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Conductor Example API v1");
        c.RoutePrefix = "swagger";
        c.DocumentTitle = "Conductor API Documentation";
        c.EnableDeepLinking();
        c.EnableFilter();
        c.EnableValidator();
    });
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();