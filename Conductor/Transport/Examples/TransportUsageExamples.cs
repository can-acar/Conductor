using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Conductor.Core;
using Conductor.Transport.Http;
using Conductor.Transport.Http.Extensions;
using Conductor.Transport.Grpc;
using Conductor.Transport.MessageQueue;

namespace Conductor.Transport.Examples;

// Example 1: Clean Controller - Transport Agnostic Business Logic
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IConductor _conductor;

    public ProductsController(IConductor conductor)
    {
        _conductor = conductor;
    }

    // Pure business logic - no transport concerns
    [HttpGet("{id}")]
    public async Task<ProductDto> GetProduct(int id, CancellationToken cancellationToken)
    {
        var query = new GetProductQuery(id);
        return await _conductor.Send<ProductDto>(query, cancellationToken);
        // Middleware automatically wraps in ApiResponse<ProductDto>
    }

    [HttpPost]
    public async Task<ProductDto> CreateProduct([FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateProductCommand(request.Name, request.Price);
        return await _conductor.Send<ProductDto>(command, cancellationToken);
    }

    [HttpDelete("{id}")]
    public async Task<bool> DeleteProduct(int id, CancellationToken cancellationToken)
    {
        var command = new DeleteProductCommand(id);
        return await _conductor.Send<bool>(command, cancellationToken);
    }
}

// Example 2: Custom Response Controller (when needed)
[ApiController]
[Route("api/custom/[controller]")]
public class CustomProductsController : ControllerBase
{
    private readonly IConductor _conductor;
    private readonly HttpResponseFormatter _responseFormatter;

    public CustomProductsController(IConductor conductor, HttpResponseFormatter responseFormatter)
    {
        _conductor = conductor;
        _responseFormatter = responseFormatter;
    }

    [HttpGet("{id}")]
    public async Task<ApiResponse<ProductDto>> GetProductWithCustomResponse(int id, CancellationToken cancellationToken)
    {
        var query = new GetProductQuery(id);
        var result = await _conductor.Send<ProductDto>(query, cancellationToken);

        // Manual response formatting for special cases
        var formattedResponse = await _responseFormatter.FormatSuccessAsync(result, new ResponseMetadata
        {
            CorrelationId = HttpContext.TraceIdentifier,
            CustomProperties = { ["RetrievedAt"] = DateTime.UtcNow }
        });

        return System.Text.Json.JsonSerializer.Deserialize<ApiResponse<ProductDto>>(formattedResponse)!;
    }
}

// Example 3: Team Standards Configuration
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Add Conductor core (transport-agnostic)
        services.AddConductor();

        // Option 1: Minimal wrapping (Team A preference)
        services.AddMinimalResponseFormatting();

        // Option 2: Standard wrapping (Team B preference)
        // services.AddStandardResponseFormatting();

        // Option 3: Enterprise wrapping (Team C preference)
        // services.AddEnterpriseResponseFormatting();

        // Option 4: Development environment
        // services.AddDevelopmentResponseFormatting();

        // Option 5: Fluent configuration
        /*
        services.ConfigureConductorResponseFormatting()
            .WrapResponses(true)
            .IncludeTimestamp()
            .IncludeCorrelationId()
            .WithSuccessMessage("Operation successful")
            .ExcludePath("/health", "/metrics")
            .AddGlobalMetadata("ApiVersion", "v1")
            .EnableStackTrace(Environment.IsDevelopment())
            .Build();
        */

        services.AddControllers();
    }

    public void Configure(IApplicationBuilder app)
    {
        // Add transport middleware
        app.UseConductorHttpTransport();

        app.UseRouting();
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}

// Example 4: Environment-Specific Configuration
public class ProductionStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddConductor();

        services.AddConductorHttpTransport(options =>
        {
            options.WrapAllResponses = true;
            options.IncludeTimestamp = true;
            options.IncludeCorrelationId = true;
            options.IncludeRequestId = true;
            options.IncludeStackTrace = false; // Never in production
            options.LogExceptions = true;
            options.SuccessMessage = "Success";

            // Production-specific exclusions
            options.ExcludedPaths.AddRange(new[] { "/health", "/metrics", "/prometheus" });

            // Global metadata
            options.GlobalMetadata["Environment"] = "Production";
            options.GlobalMetadata["ApiVersion"] = "v1.0";
        });
    }
}

public class DevelopmentStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddConductor();

        services.AddConductorHttpTransport(options =>
        {
            options.WrapAllResponses = true;
            options.IncludeTimestamp = true;
            options.IncludeCorrelationId = true;
            options.IncludeRequestId = true;
            options.IncludeStackTrace = true; // Enable in development
            options.LogExceptions = true;
            options.SuccessMessage = "Development Success";

            options.GlobalMetadata["Environment"] = "Development";
            options.GlobalMetadata["Debug"] = true;
        });
    }
}

// Example 5: Multi-Transport Support
public class MultiTransportStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Core Conductor (transport-agnostic)
        services.AddConductor();

        // HTTP Transport
        services.AddConductorHttpTransport();

        // gRPC Transport
        services.AddScoped<GrpcResponseFormatter>();
        services.AddScoped<GrpcResponseMetadataProvider>();

        // Message Queue Transport
        services.AddScoped<MessageQueueFormatter>();
        services.AddScoped<MessageQueueMetadataProvider>();
        services.AddScoped<IMessageQueuePublisher, RabbitMqPublisher>();

        services.AddControllers();
        services.AddGrpc();
    }

    public void Configure(IApplicationBuilder app)
    {
        // HTTP middleware
        app.UseConductorHttpTransport();

        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapGrpcService<ProductGrpcService>();
        });
    }
}

// Example 6: Raw vs Wrapped Response Demo
[ApiController]
[Route("api/demo")]
public class ResponseDemoController : ControllerBase
{
    private readonly IConductor _conductor;

    public ResponseDemoController(IConductor conductor)
    {
        _conductor = conductor;
    }

    // This will be automatically wrapped by middleware
    [HttpGet("wrapped/{id}")]
    public async Task<ProductDto> GetWrappedProduct(int id)
    {
        return await _conductor.Send<ProductDto>(new GetProductQuery(id));
        // Response: { "success": true, "data": { "id": 1, "name": "Product" }, "message": "Success" }
    }

    // This returns raw ApiResponse (no middleware wrapping)
    [HttpGet("manual/{id}")]
    public async Task<ApiResponse<ProductDto>> GetManualProduct(int id)
    {
        var product = await _conductor.Send<ProductDto>(new GetProductQuery(id));
        return ApiResponse<ProductDto>.CreateSuccess(product, "Manually wrapped");
        // Response: { "success": true, "data": { "id": 1, "name": "Product" }, "message": "Manually wrapped" }
    }

    // This bypasses middleware wrapping (via configuration)
    [HttpGet("raw/{id}")]
    public async Task<ProductDto> GetRawProduct(int id)
    {
        // Add [SkipResponseFormatting] attribute or configure exclusion
        return await _conductor.Send<ProductDto>(new GetProductQuery(id));
        // Response: { "id": 1, "name": "Product" }
    }
}

// Example 7: gRPC Service Integration
public class ProductGrpcService // : ProductService.ProductServiceBase (proto-generated)
{
    private readonly IConductor _conductor;
    private readonly GrpcResponseFormatter _responseFormatter;

    public ProductGrpcService(IConductor conductor, GrpcResponseFormatter responseFormatter)
    {
        _conductor = conductor;
        _responseFormatter = responseFormatter;
    }

    // public override async Task<GetProductResponse> GetProduct(GetProductRequest request, ServerCallContext context)
    // {
    //     var query = new GetProductQuery(request.Id);
    //     var result = await _conductor.Send<ProductDto>(query, context.CancellationToken);
    //
    //     // Format response (adds metadata to headers)
    //     await _responseFormatter.FormatSuccessAsync(result, null, context.CancellationToken);
    //
    //     return new GetProductResponse
    //     {
    //         Product = new Product { Id = result.Id, Name = result.Name }
    //     };
    // }
}

// Example 8: Message Queue Consumer
public class ProductMessageConsumer
{
    private readonly MessageQueueConductorService _messageService;

    public ProductMessageConsumer(MessageQueueConductorService messageService)
    {
        _messageService = messageService;
    }

    public async Task HandleCreateProductMessage(CreateProductMessage message)
    {
        var command = new CreateProductCommand(message.Name, message.Price);

        await _messageService.HandleMessageAsync<CreateProductCommand, ProductDto>(
            command,
            message.Headers);

        // Response automatically published to appropriate routing key
        // Success: "response.success.productdto"
        // Error: "response.error.validationexception"
    }
}

// Supporting classes
public class GetProductQuery : Core.BaseRequest
{
    public int ProductId { get; }
    public GetProductQuery(int productId) => ProductId = productId;
}

public class CreateProductCommand : Core.BaseRequest
{
    public string Name { get; }
    public decimal Price { get; }
    public CreateProductCommand(string name, decimal price) => (Name, Price) = (name, price);
}

public class DeleteProductCommand : Core.BaseRequest
{
    public int ProductId { get; }
    public DeleteProductCommand(int productId) => ProductId = productId;
}

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class CreateProductMessage
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public Dictionary<string, object> Headers { get; set; } = new();
}