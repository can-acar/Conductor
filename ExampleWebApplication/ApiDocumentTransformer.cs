using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace ExampleWebApplication;

public class ApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Set API information
        document.Info = new OpenApiInfo
        {
            Title = "Conductor Example API",
            Version = "v1",
            Description = "A comprehensive API demonstrating the Conductor framework's mediator pattern capabilities including commands, queries, events, and pipelines.",
            Contact = new OpenApiContact
            {
                Name = "Conductor Framework",
                Email = "support@conductor.example.com",
                Url = new Uri("https://github.com/conductor/conductor")
            },
            License = new OpenApiLicense
            {
                Name = "MIT License",
                Url = new Uri("https://opensource.org/licenses/MIT")
            }
        };

        // Add servers
        document.Servers = new List<OpenApiServer>
        {
            new OpenApiServer
            {
                Url = "https://localhost:5001",
                Description = "Development HTTPS server"
            },
            new OpenApiServer
            {
                Url = "http://localhost:5000",
                Description = "Development HTTP server"
            }
        };

        // Add tags for better organization
        document.Tags = new List<OpenApiTag>
        {
            new OpenApiTag
            {
                Name = "Products",
                Description = "Product management operations using Conductor framework"
            }
        };

        // Add security schemes if needed
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Enter JWT token for authorization"
            }
        };

        return Task.CompletedTask;
    }
}