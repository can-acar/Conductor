using Conductor.Attributes;
using Conductor.Pipeline;

namespace ExampleWebApplication;

// Example: Simple cacheable query
[Cacheable(DurationSeconds = 600, UseRequestData = true)]
public class GetProductQuery : CacheableRequest
{
    public int ProductId { get; set; }

    public GetProductQuery(int productId)
    {
        ProductId = productId;
    }

    public override string GetCacheKey()
    {
        return $"Product_{ProductId}";
    }
}

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

// Example: Transactional command
[Transactional(RequireTransaction = true)]
[Auditable(LogRequestData = true, Category = "ProductManagement")]
public class CreateProductCommand : TransactionalRequest
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;

    public CreateProductCommand(string name, decimal price, string category)
    {
        Name = name;
        Price = price;
        Category = category;
    }
}

// Example: Authorized command
[Authorize("Products.Create", "Admin")]
[Auditable(LogRequestData = true, LogResponseData = true, Category = "Security")]
public class DeleteProductCommand : AuthorizedRequest
{
    public int ProductId { get; set; }

    public DeleteProductCommand(int productId)
    {
        ProductId = productId;
    }
}

// Example: Full pipeline request
[Cacheable(DurationSeconds = 300)]
[Transactional]
[Authorize("Reports.View")]
[Auditable(LogRequestData = false, Category = "Reporting")]
public class GenerateProductReportQuery : FullPipelineRequest
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Format { get; set; } = "PDF";

    public GenerateProductReportQuery(DateTime startDate, DateTime endDate, string format = "PDF")
    {
        StartDate = startDate;
        EndDate = endDate;
        Format = format;
    }

    public override string GetCacheKey()
    {
        return $"ProductReport_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}_{Format}";
    }

    public override string GetAuditDetails()
    {
        return $"Generated product report from {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd} in {Format} format";
    }
}

// Example handlers
public class ProductQueryHandler
{
    [Handle]
    public async Task<ProductDto> GetProduct(GetProductQuery query, CancellationToken cancellationToken = default)
    {
        // Simulate async work
        await Task.Delay(100, cancellationToken);

        return new ProductDto
        {
            Id = query.ProductId,
            Name = $"Product {query.ProductId}",
            Price = 29.99m
        };
    }
}

public class ProductCommandHandler
{
    [Handle]
    public async Task<ProductDto> CreateProduct(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        // Simulate database insert within transaction
        await Task.Delay(200, cancellationToken);

        return new ProductDto
        {
            Id = Random.Shared.Next(1000, 9999),
            Name = command.Name,
            Price = command.Price
        };
    }

    [Handle]
    public async Task<bool> DeleteProduct(DeleteProductCommand command, CancellationToken cancellationToken = default)
    {
        // Simulate database delete with authorization check
        await Task.Delay(150, cancellationToken);

        return true;
    }
}

public class ReportHandler
{
    [Handle]
    public async Task<byte[]> GenerateReport(GenerateProductReportQuery query, CancellationToken cancellationToken = default)
    {
        // Simulate report generation
        await Task.Delay(2000, cancellationToken);

        // Return dummy PDF content
        return System.Text.Encoding.UTF8.GetBytes($"Product Report {query.StartDate:yyyy-MM-dd} to {query.EndDate:yyyy-MM-dd}");
    }
}

// Example: Custom pipeline behavior for specific request type
public class ProductCachingBehavior : IPipelineBehavior<GetProductQuery, ProductDto>
{
    private readonly Microsoft.Extensions.Logging.ILogger<ProductCachingBehavior> _logger;

    public ProductCachingBehavior(Microsoft.Extensions.Logging.ILogger<ProductCachingBehavior> logger)
    {
        _logger = logger;
    }

    public async Task<ProductDto> Handle(GetProductQuery request, RequestHandlerDelegate<ProductDto> next, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Custom product caching behavior for product {ProductId}", request.ProductId);

        // Custom caching logic specific to products
        var response = await next();

        _logger.LogInformation("Product {ProductId} retrieved: {ProductName}", response.Id, response.Name);

        return response;
    }
}

// Example: Request-specific pre-processor
public class ProductValidationPreProcessor : IRequestPreProcessor<CreateProductCommand>
{
    private readonly Microsoft.Extensions.Logging.ILogger<ProductValidationPreProcessor> _logger;

    public ProductValidationPreProcessor(Microsoft.Extensions.Logging.ILogger<ProductValidationPreProcessor> logger)
    {
        _logger = logger;
    }

    public Task Process(CreateProductCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Pre-processing product creation for {ProductName}", request.Name);

        // Custom validation logic
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Product name cannot be empty");
        }

        if (request.Price <= 0)
        {
            throw new ArgumentException("Product price must be greater than zero");
        }

        return Task.CompletedTask;
    }
}

// Example: Request-specific post-processor
public class ProductCreationPostProcessor : IRequestPostProcessor<CreateProductCommand, ProductDto>
{
    private readonly Microsoft.Extensions.Logging.ILogger<ProductCreationPostProcessor> _logger;

    public ProductCreationPostProcessor(Microsoft.Extensions.Logging.ILogger<ProductCreationPostProcessor> logger)
    {
        _logger = logger;
    }

    public Task Process(CreateProductCommand request, ProductDto response, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Product {ProductId} created successfully with name {ProductName}",
            response.Id, response.Name);

        // Send notification, update cache, etc.

        return Task.CompletedTask;
    }
}

// Usage example in Startup/Program.cs:
/*
public static class PipelineConfiguration
{
    public static IServiceCollection ConfigurePipelineExample(this IServiceCollection services)
    {
        // Add Conductor with Pipeline
        services.AddConductorWithPipeline();

        // Register handlers
        services.AddScoped<ProductQueryHandler>();
        services.AddScoped<ProductCommandHandler>();
        services.AddScoped<ReportHandler>();

        // Add custom behaviors
        services.AddPipelineBehavior<ProductCachingBehavior>();

        // Add pre/post processors
        services.AddRequestPreProcessor<CreateProductCommand, ProductValidationPreProcessor>();
        services.AddRequestPostProcessor<CreateProductCommand, ProductDto, ProductCreationPostProcessor>();

        // Configure performance monitoring
        services.ConfigurePipelinePerformance(TimeSpan.FromMilliseconds(1000));

        // Add optional behaviors
        services.AddAuthorizationBehavior();
        services.AddAuditingBehavior();
        services.AddCachingBehavior();
        services.AddTransactionBehavior();

        return services;
    }
}

// Usage in controller:
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IConductor _conductor;

    public ProductsController(IConductor conductor)
    {
        _conductor = conductor;
    }

    [HttpGet("{id}")]
    public async Task<ProductDto> GetProduct(int id, CancellationToken cancellationToken)
    {
        var query = new GetProductQuery(id);
        return await _conductor.Send<ProductDto>(query, cancellationToken);
    }

    [HttpPost]
    public async Task<ProductDto> CreateProduct([FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateProductCommand(request.Name, request.Price, request.Category);
        return await _conductor.Send<ProductDto>(command, cancellationToken);
    }

    [HttpDelete("{id}")]
    public async Task<bool> DeleteProduct(int id, CancellationToken cancellationToken)
    {
        var command = new DeleteProductCommand(id);
        return await _conductor.Send<bool>(command, cancellationToken);
    }
}
*/