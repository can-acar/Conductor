using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Conductor.Core;
using Conductor.Attributes;
using Conductor.Pipeline;
using Conductor.Transport.Http;
using Conductor.Transport.Http.Extensions;
using Conductor.Validation;
using Conductor.Saga;

namespace Conductor.Examples;

// Example demonstrating the complete Conductor ecosystem:
// ✅ Transport-agnostic core
// ✅ Pipeline behaviors
// ✅ FluentValidation
// ✅ Expert-level Saga
// ✅ HTTP transport layer
// ✅ Multi-transport support

#region Domain Models

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

#endregion

#region Requests and Validation

// Cacheable query with validation
[Cacheable(DurationSeconds = 300)]
public class GetProductQuery : CacheableRequest
{
    public int ProductId { get; set; }

    public GetProductQuery(int productId)
    {
        ProductId = productId;
    }

    public override string GetCacheKey() => $"Product_{ProductId}";
}

public class GetProductQueryValidator : AbstractValidator<GetProductQuery>
{
    public GetProductQueryValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0)
            .WithMessage("Product ID must be greater than 0");
    }
}

// Transactional command with authorization and auditing
[Transactional]
[Authorize("Products.Create")]
[Auditable(LogRequestData = true, Category = "ProductManagement")]
public class CreateProductCommand : AuthorizedAuditableRequest
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

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Product name is required")
            .MaximumLength(100)
            .WithMessage("Product name cannot exceed 100 characters");

        RuleFor(x => x.Price)
            .GreaterThan(0)
            .WithMessage("Price must be greater than 0")
            .LessThanOrEqualTo(10000)
            .WithMessage("Price cannot exceed 10,000");

        RuleFor(x => x.Category)
            .NotEmpty()
            .WithMessage("Category is required")
            .Must(BeValidCategory)
            .WithMessage("Invalid category");
    }

    private bool BeValidCategory(string category)
    {
        var validCategories = new[] { "Electronics", "Books", "Clothing", "Home" };
        return validCategories.Contains(category);
    }
}

// Saga-orchestrated process
public class ProcessOrderSaga : FullPipelineRequest
{
    public int OrderId { get; set; }
    public List<int> ProductIds { get; set; } = new();
    public string CustomerEmail { get; set; } = string.Empty;

    public ProcessOrderSaga(int orderId, List<int> productIds, string customerEmail)
    {
        OrderId = orderId;
        ProductIds = productIds;
        CustomerEmail = customerEmail;
    }
}

public class ProcessOrderSagaState : DefaultSagaState
{
    public int OrderId { get; set; }
    public List<int> ProductIds { get; set; } = new();
    public string CustomerEmail { get; set; } = string.Empty;
    public List<int> ReservedProductIds { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public bool PaymentProcessed { get; set; }
    public bool EmailSent { get; set; }
}

#endregion

#region Handlers

public class ProductQueryHandler
{
    private readonly ILogger<ProductQueryHandler> _logger;

    public ProductQueryHandler(ILogger<ProductQueryHandler> logger)
    {
        _logger = logger;
    }

    [Handle]
    public async Task<ProductDto> GetProduct(GetProductQuery query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving product {ProductId}", query.ProductId);

        // Simulate database call
        await Task.Delay(100, cancellationToken);

        // Simulate product retrieval
        var product = new Product
        {
            Id = query.ProductId,
            Name = $"Product {query.ProductId}",
            Price = 29.99m,
            Category = "Electronics",
            IsActive = true
        };

        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            Category = product.Category,
            IsActive = product.IsActive
        };
    }
}

public class ProductCommandHandler
{
    private readonly ILogger<ProductCommandHandler> _logger;

    public ProductCommandHandler(ILogger<ProductCommandHandler> logger)
    {
        _logger = logger;
    }

    [Handle]
    public async Task<ProductDto> CreateProduct(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating product {ProductName} in category {Category}",
            command.Name, command.Category);

        // Simulate database transaction
        await Task.Delay(200, cancellationToken);

        var product = new Product
        {
            Id = Random.Shared.Next(1000, 9999),
            Name = command.Name,
            Price = command.Price,
            Category = command.Category,
            IsActive = true
        };

        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            Category = product.Category,
            IsActive = product.IsActive
        };
    }
}

#endregion

#region Saga Implementation

public class ProcessOrderSagaOrchestrator : SagaOrchestrator<ProcessOrderSagaState>
{
    public ProcessOrderSagaOrchestrator(
        ISagaPersistence<ProcessOrderSagaState> persistence,
        ISagaMonitor monitor,
        IServiceProvider serviceProvider,
        ILogger<ProcessOrderSagaOrchestrator> logger)
        : base(persistence, monitor, serviceProvider, logger)
    {
    }

    protected void ConfigureSteps()
    {
        AddStep("ReserveProducts", async (state, cancellationToken) =>
        {
            // Reserve products in inventory
            await Task.Delay(500, cancellationToken);
            state.ReservedProductIds = state.ProductIds.ToList();
            return SagaStepResult.Success(nextStep: "CalculateTotal");
        });

        AddStep("CalculateTotal", async (state, cancellationToken) =>
        {
            // Calculate order total
            await Task.Delay(200, cancellationToken);
            state.TotalAmount = state.ProductIds.Count * 29.99m;
            return SagaStepResult.Success(nextStep: "ProcessPayment");
        });

        AddStep("ProcessPayment", async (state, cancellationToken) =>
        {
            // Process payment
            await Task.Delay(1000, cancellationToken);

            // Simulate random payment failure for demo
            if (Random.Shared.NextDouble() < 0.2) // 20% chance of failure
            {
                return SagaStepResult.Compensate("Payment failed");
            }

            state.PaymentProcessed = true;
            return SagaStepResult.Success(nextStep: "SendConfirmationEmail");
        });

        AddStep("SendConfirmationEmail", async (state, cancellationToken) =>
        {
            // Send confirmation email
            await Task.Delay(300, cancellationToken);
            state.EmailSent = true;
            return SagaStepResult.Complete();
        });

        // Compensation steps
        AddCompensationStep("ProcessPayment", async (state, cancellationToken) =>
        {
            // Refund payment if already processed
            if (state.PaymentProcessed)
            {
                await Task.Delay(500, cancellationToken);
                state.PaymentProcessed = false;
            }
            return SagaStepResult.Success();
        });

        AddCompensationStep("ReserveProducts", async (state, cancellationToken) =>
        {
            // Release reserved products
            await Task.Delay(300, cancellationToken);
            state.ReservedProductIds.Clear();
            return SagaStepResult.Success();
        });
    }
}

public class ProcessOrderSagaHandler
{
    private readonly ISagaOrchestrator<ProcessOrderSagaState> _orchestrator;
    private readonly ILogger<ProcessOrderSagaHandler> _logger;

    public ProcessOrderSagaHandler(
        ISagaOrchestrator<ProcessOrderSagaState> orchestrator,
        ILogger<ProcessOrderSagaHandler> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    [Handle]
    public async Task<ProcessOrderSagaState> ProcessOrder(ProcessOrderSaga command, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting order processing saga for order {OrderId}", command.OrderId);

        var sagaState = new ProcessOrderSagaState
        {
            SagaType = nameof(ProcessOrderSaga),
            OrderId = command.OrderId,
            ProductIds = command.ProductIds,
            CustomerEmail = command.CustomerEmail,
            Status = SagaStatus.Running,
            Metadata = new SagaMetadata
            {
                InitiatedBy = command.UserId,
                BusinessContext = "OrderProcessing",
                Timeout = TimeSpan.FromMinutes(10),
                TimeoutAction = "Compensate"
            }
        };

        return await _orchestrator.StartAsync(sagaState, cancellationToken);
    }
}

#endregion

#region Controllers

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IConductor _conductor;

    public ProductsController(IConductor conductor)
    {
        _conductor = conductor;
    }

    /// <summary>
    /// Get product by ID (cacheable, validated)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ProductDto> GetProduct(int id, CancellationToken cancellationToken)
    {
        var query = new GetProductQuery(id);
        return await _conductor.Send<ProductDto>(query, cancellationToken);
        // Automatic wrapping: ApiResponse<ProductDto>
        // Automatic caching (300 seconds)
        // Automatic validation
        // Automatic logging & performance monitoring
    }

    /// <summary>
    /// Create new product (transactional, authorized, audited)
    /// </summary>
    [HttpPost]
    public async Task<ProductDto> CreateProduct([FromBody] CreateProductRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateProductCommand(request.Name, request.Price, request.Category);
        return await _conductor.Send<ProductDto>(command, cancellationToken);
        // Automatic transaction management
        // Automatic authorization check
        // Automatic audit logging
        // Automatic validation
    }

    /// <summary>
    /// Process order using Saga pattern
    /// </summary>
    [HttpPost("process-order")]
    public async Task<ProcessOrderSagaState> ProcessOrder([FromBody] ProcessOrderRequest request, CancellationToken cancellationToken)
    {
        var saga = new ProcessOrderSaga(request.OrderId, request.ProductIds, request.CustomerEmail);
        return await _conductor.Send<ProcessOrderSagaState>(saga, cancellationToken);
        // Automatic saga orchestration
        // Compensation on failure
        // Step-by-step execution
        // Monitoring and diagnostics
    }
}

[ApiController]
[Route("api/saga")]
public class SagaController : ControllerBase
{
    private readonly ISagaMonitor _sagaMonitor;
    private readonly ISagaDiagnosticService _sagaDiagnostics;

    public SagaController(ISagaMonitor sagaMonitor, ISagaDiagnosticService sagaDiagnostics)
    {
        _sagaMonitor = sagaMonitor;
        _sagaDiagnostics = sagaDiagnostics;
    }

    [HttpGet("health")]
    public async Task<SagaHealthReport> GetSagaHealth()
    {
        return await _sagaMonitor.GetHealthReportAsync();
    }

    [HttpGet("{sagaId}/report")]
    public async Task<SagaDiagnosticReport> GetSagaReport(Guid sagaId)
    {
        return await _sagaDiagnostics.GenerateReportAsync(sagaId);
    }

    [HttpGet("performance")]
    public async Task<SagaPerformanceMetrics> GetPerformanceMetrics([FromQuery] string? sagaType = null)
    {
        return await _sagaMonitor.GetPerformanceMetricsAsync(sagaType);
    }
}

#endregion

#region Startup Configuration

public class ComprehensiveStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // 1. Core Conductor (transport-agnostic)
        services.AddConductor();

        // 2. Pipeline Behaviors
        services.AddConductorPipeline();
        services.AddAuthorizationBehavior();
        services.AddAuditingBehavior();
        services.AddCachingBehavior();
        services.AddTransactionBehavior();

        // 3. FluentValidation
        services.AddScoped<IValidator<GetProductQuery>, GetProductQueryValidator>();
        services.AddScoped<IValidator<CreateProductCommand>, CreateProductCommandValidator>();

        // 4. Expert Saga System
        services.AddScoped<ISagaPersistence<ProcessOrderSagaState>, InMemorySagaPersistence<ProcessOrderSagaState>>();
        services.AddScoped<ISagaOrchestrator<ProcessOrderSagaState>, ProcessOrderSagaOrchestrator>();
        services.AddSingleton<ISagaMonitor, SagaMonitor>();
        services.AddScoped<ISagaDiagnosticService, SagaDiagnosticService>();
        services.AddHostedService<SagaTimeoutManager>();

        // 5. Handlers
        services.AddScoped<ProductQueryHandler>();
        services.AddScoped<ProductCommandHandler>();
        services.AddScoped<ProcessOrderSagaHandler>();

        // 6. HTTP Transport Layer
        services.AddEnterpriseResponseFormatting();

        // 7. ASP.NET Core
        services.AddControllers();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        // 8. Swagger for API documentation
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }

    public void Configure(IApplicationBuilder app)
    {
        // Development middleware would go here
        app.UseSwagger();
        app.UseSwaggerUI();

        // Transport middleware pipeline
        app.UseConductorHttpTransport();

        app.UseRouting();
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}

#endregion

#region Supporting Classes

public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
}

public class ProcessOrderRequest
{
    public int OrderId { get; set; }
    public List<int> ProductIds { get; set; } = new();
    public string CustomerEmail { get; set; } = string.Empty;
}

#endregion

// Usage Examples:
/*
// 1. Simple Product Query (GET /api/products/123)
// Request → Validation → Cache Check → Handler → Response Wrapping
// Response: {
//   "success": true,
//   "data": { "id": 123, "name": "Product 123", "price": 29.99 },
//   "message": "Operation completed successfully",
//   "timestamp": "2023-...",
//   "correlationId": "abc-123"
// }

// 2. Product Creation (POST /api/products)
// Request → Validation → Authorization → Transaction Begin → Handler → Audit → Transaction Commit → Response
// All cross-cutting concerns handled automatically

// 3. Saga Orchestration (POST /api/products/process-order)
// Request → Saga Start → Step 1 (Reserve) → Step 2 (Calculate) → Step 3 (Payment) → Step 4 (Email)
// If any step fails → Automatic compensation in reverse order
// Full monitoring and diagnostics available at /api/saga/health

// 4. Multi-Transport Support
// Same business logic works for:
// - HTTP API (ApiResponse<T> wrapping)
// - gRPC (Metadata in headers)
// - Message Queue (Routed messages with headers)
*/