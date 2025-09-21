# Conductor Framework

[![.NET](https://img.shields.io/badge/.NET-9.0+-purple.svg)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A powerful, lightweight .NET framework implementing the mediator pattern with advanced features including pipelines, caching, validation, transactions, auditing, and saga orchestration.

> **Note**: This framework requires .NET 9.0 or higher. If you're using an older version of .NET, you may need to adjust the target framework in the project files.

## 📑 Table of Contents

- [✨ Key Features](#-key-features)
- [🚀 Quick Start](#-quick-start)
- [🏗️ Architecture Overview](#️-architecture-overview)
- [📖 Detailed Usage Examples](#-detailed-usage-examples)
  - [Caching](#caching)
  - [Validation](#validation)
  - [Transactions](#transactions)
  - [Auditing](#auditing)
  - [Authorization](#authorization)
  - [Pipeline Behaviors](#pipeline-behaviors)
  - [Saga Orchestration](#saga-orchestration)
  - [Events](#events)
- [⚙️ Configuration](#️-configuration)
- [🧪 Testing](#-testing)
- [🔧 Advanced Features](#-advanced-features)
- [🚀 Performance Considerations](#-performance-considerations)
- [🤝 Contributing](#-contributing)
- [📄 License](#-license)
- [🆘 Support](#-support)

## ✨ Key Features

- **🎯 Mediator Pattern**: Clean separation of concerns with request/response handling
- **⚡ Caching**: Built-in memory caching with configurable expiration
- **🔒 Validation**: Automatic request/response validation with FluentValidation support
- **💼 Transactions**: Declarative transaction management
- **📊 Auditing**: Comprehensive audit logging with customizable levels
- **🔐 Authorization**: Declarative authorization with role-based access control
- **🚀 Pipeline Behaviors**: Extensible pipeline with pre/post processors
- **🎭 Saga Orchestration**: Long-running workflows with compensation patterns
- **🌐 Multi-Transport**: HTTP API, gRPC, and message queue support
- **🔧 Middleware Integration**: ASP.NET Core middleware for response formatting and exception handling

## 🚀 Quick Start

### Installation

Add the Conductor framework to your .NET 9.0+ project:

```xml
<ProjectReference Include="path/to/Conductor/Conductor.csproj" />
```

Or install via NuGet when available:

```bash
dotnet add package Conductor.Framework
```

### Basic Setup

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Conductor services
builder.Services.AddConductor(options =>
{
    options.HandlerAssemblies.Add(typeof(Program).Assembly);
    options.EnableCaching = true;
    options.EnablePipelining = true;
    options.DefaultCacheExpiration = TimeSpan.FromMinutes(5);
});

// Add controllers
builder.Services.AddControllers();

var app = builder.Build();

// Initialize Conductor
Conductor.Extensions.Conductor.Init(app.Services);

// Add middleware
app.UseGlobalExceptionHandling();
app.UseResponseFormatter();

app.UseRouting();
app.MapControllers();
app.Run();
```

### Create a Simple Query

```csharp
// Define a query
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

// Define the response DTO
public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

// Create a handler
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

// Use in a controller
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
}
```

## 🏗️ Architecture Overview

The Conductor framework follows a layered architecture with clean separation of concerns:

```
┌─────────────────────────────────────────────────────────────┐
│                    Transport Layer                          │
│  (HTTP Controllers, gRPC, Message Queues)                 │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                 Conductor Core                              │
│  • IConductor Interface                                     │
│  • Request/Response Routing                                 │
│  • Handler Discovery & Execution                           │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                Pipeline Middleware                          │
│  • Caching • Validation • Authorization                    │
│  • Transactions • Auditing • Custom Behaviors              │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                Business Handlers                            │
│  • Query Handlers • Command Handlers                       │
│  • Event Handlers • Saga Orchestrators                     │
└─────────────────────────────────────────────────────────────┘
```

## 📖 Detailed Usage Examples

### Caching

Automatic caching with customizable expiration and cache keys:

```csharp
[Cacheable(DurationSeconds = 300, SlidingExpiration = true)]
public class GetUserProfileQuery : CacheableRequest
{
    public string UserId { get; set; }

    public override string GetCacheKey()
    {
        return $"UserProfile_{UserId}";
    }
}
```

### Validation

Automatic request/response validation with FluentValidation:

```csharp
[Validate(ValidateRequest = true, ThrowOnValidationError = true)]
public class CreateProductCommand : BaseRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, double.MaxValue)]
    public decimal Price { get; set; }
}

// Custom validator
public class CreateProductCommandValidator : IValidator<CreateProductCommand>
{
    public async Task<ValidationResult> ValidateAsync(CreateProductCommand request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ValidationResult.Failure(new ValidationError("Name", "Name is required", "Required"));

        if (request.Price <= 0)
            return ValidationResult.Failure(new ValidationError("Price", "Price must be greater than 0", "Range"));

        return ValidationResult.Success();
    }
}
```

### Transactions

Declarative transaction management:

```csharp
[Transactional(RequireTransaction = true, IsolationLevel = IsolationLevel.ReadCommitted)]
public class CreateOrderCommand : TransactionalRequest
{
    public string CustomerId { get; set; }
    public List<OrderItem> Items { get; set; }
}

public class OrderHandler
{
    [Handle]
    public async Task<Order> CreateOrder(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        // This method runs within a transaction
        // If an exception occurs, the transaction will rollback automatically
        
        var order = new Order { CustomerId = command.CustomerId };
        // Save order to database
        
        foreach (var item in command.Items)
        {
            // Process each item
            // All operations are within the same transaction
        }
        
        return order;
    }
}
```

### Auditing

Comprehensive audit logging:

```csharp
[Auditable(LogRequest = true, LogResponse = true, Category = "UserManagement", Level = AuditLevel.Information)]
public class UpdateUserCommand : BaseRequest
{
    public string UserId { get; set; }
    public string Email { get; set; }
}

public class UserHandler
{
    [Handle]
    public async Task<User> UpdateUser(UpdateUserCommand command)
    {
        // Handler execution is automatically audited
        // Audit entry includes: timestamp, user, request/response data, execution time
        return await _userService.UpdateAsync(command.UserId, command.Email);
    }
}
```

### Authorization

Role-based access control:

```csharp
[Authorize("Users.Update", "Admin")]
[Auditable(Category = "Security")]
public class DeleteUserCommand : AuthorizedRequest
{
    public string UserId { get; set; }
}

public class UserHandler
{
    [Handle]
    public async Task<bool> DeleteUser(DeleteUserCommand command)
    {
        // Only users with "Users.Update" permission or "Admin" role can execute this
        return await _userService.DeleteAsync(command.UserId);
    }
}
```

### Pipeline Behaviors

Custom pipeline behaviors for cross-cutting concerns:

```csharp
public class PerformanceMonitoringBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : BaseRequest
{
    private readonly ILogger<PerformanceMonitoringBehavior<TRequest, TResponse>> _logger;

    public PerformanceMonitoringBehavior(ILogger<PerformanceMonitoringBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        var response = await next();
        
        stopwatch.Stop();
        
        if (stopwatch.ElapsedMilliseconds > 1000) // Log slow requests
        {
            _logger.LogWarning("Slow request detected: {RequestType} took {ElapsedMs}ms", 
                typeof(TRequest).Name, stopwatch.ElapsedMilliseconds);
        }
        
        return response;
    }
}

// Register in DI container
services.AddPipelineBehavior<PerformanceMonitoringBehavior<,>>();
```

### Saga Orchestration

Long-running workflow management with compensation:

```csharp
public class ProcessOrderSaga : FullPipelineRequest
{
    public int OrderId { get; set; }
    public List<int> ProductIds { get; set; } = new();
    public string CustomerEmail { get; set; } = string.Empty;
}

public class ProcessOrderSagaState : DefaultSagaState
{
    public int OrderId { get; set; }
    public List<int> ReservedProductIds { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public bool PaymentProcessed { get; set; }
    public bool EmailSent { get; set; }
}

public class ProcessOrderSagaOrchestrator : SagaOrchestrator<ProcessOrderSagaState>
{
    protected void ConfigureSteps()
    {
        AddStep("ReserveProducts", async (state, cancellationToken) =>
        {
            // Reserve products in inventory
            state.ReservedProductIds = state.ProductIds.ToList();
            return SagaStepResult.Success(nextStep: "CalculateTotal");
        });

        AddStep("CalculateTotal", async (state, cancellationToken) =>
        {
            // Calculate order total
            state.TotalAmount = state.ProductIds.Count * 29.99m;
            return SagaStepResult.Success(nextStep: "ProcessPayment");
        });

        AddStep("ProcessPayment", async (state, cancellationToken) =>
        {
            // Process payment
            if (Random.Shared.NextDouble() < 0.2) // Simulate failure
            {
                return SagaStepResult.Failure("Payment failed");
            }
            
            state.PaymentProcessed = true;
            return SagaStepResult.Success(nextStep: "SendEmail");
        });

        AddStep("SendEmail", async (state, cancellationToken) =>
        {
            // Send confirmation email
            state.EmailSent = true;
            return SagaStepResult.Success();
        });

        // Compensation steps (executed in reverse order on failure)
        AddCompensationStep("ReserveProducts", async (state, cancellationToken) =>
        {
            // Release reserved products
            state.ReservedProductIds.Clear();
            return SagaStepResult.Success();
        });
    }
}
```

### Events

Event publishing and handling:

```csharp
// Define an event
public class ProductCreatedEvent : Event<Product>
{
    public ProductCreatedEvent(Product product) : base(product)
    {
    }
}

// Event handler
public class ProductEventHandlers
{
    [Saga]
    public async Task OnProductCreated(ProductCreatedEvent eventData)
    {
        // Handle the event (e.g., update search index, send notifications)
        var product = eventData.Data;
        // Process the product creation event
    }
}

// Publish event from handler
public class ProductHandler
{
    private readonly IConductor _conductor;

    [Handle]
    public async Task<Product> CreateProduct(CreateProductCommand command)
    {
        var product = new Product { Name = command.Name, Price = command.Price };
        
        // Save to database
        await _repository.SaveAsync(product);
        
        // Publish event
        await _conductor.Publish(new ProductCreatedEvent(product));
        
        return product;
    }
}
```

## ⚙️ Configuration

### Conductor Options

```csharp
services.AddConductor(options =>
{
    // Register assemblies containing handlers
    options.HandlerAssemblies.Add(typeof(ProductHandler).Assembly);
    options.HandlerAssemblies.Add(typeof(UserHandler).Assembly);
    
    // Enable features
    options.EnableCaching = true;
    options.EnablePipelining = true;
    options.EnableValidation = true;
    options.EnableAuditing = true;
    
    // Default cache settings
    options.DefaultCacheExpiration = TimeSpan.FromMinutes(10);
    
    // Performance settings
    options.DefaultRequestTimeout = TimeSpan.FromSeconds(30);
});
```

### Pipeline Configuration

```csharp
// Add pipeline behaviors in order of execution
services.AddConductorPipeline(pipeline =>
{
    pipeline.AddAuthorizationBehavior();
    pipeline.AddValidationBehavior();
    pipeline.AddCachingBehavior();
    pipeline.AddTransactionBehavior();
    pipeline.AddAuditingBehavior();
    pipeline.AddPerformanceMonitoringBehavior();
});
```

### Middleware Configuration

```csharp
// Configure response formatting
app.UseResponseFormatter(options =>
{
    options.WrapResponses = true;
    options.IncludeMetadata = true;
    options.DefaultSuccessMessage = "Operation completed successfully";
});

// Configure exception handling
app.UseGlobalExceptionHandling(options =>
{
    options.IncludeStackTrace = app.Environment.IsDevelopment();
    options.LogExceptions = true;
});
```

## 🧪 Testing

The framework supports easy unit testing by allowing direct handler testing without the full pipeline:

```csharp
[Test]
public async Task GetProduct_ShouldReturnProduct()
{
    // Arrange
    var handler = new ProductQueryHandler();
    var query = new GetProductQuery(123);
    
    // Act
    var result = await handler.GetProduct(query);
    
    // Assert
    Assert.That(result.Id, Is.EqualTo(123));
    Assert.That(result.Name, Is.EqualTo("Product 123"));
}

[Test]
public async Task CreateProduct_WithInvalidData_ShouldThrowValidationException()
{
    // Arrange
    var conductor = serviceProvider.GetService<IConductor>();
    var command = new CreateProductCommand("", -1, ""); // Invalid data
    
    // Act & Assert
    await Assert.ThrowsAsync<ValidationException>(() => 
        conductor.Send<ProductDto>(command));
}
```

## 🔧 Advanced Features

### Custom Request Types

Create specialized request base classes for different scenarios:

```csharp
// Cacheable requests
public abstract class CacheableRequest : BaseRequest
{
    public abstract string GetCacheKey();
}

// Authorized requests
public abstract class AuthorizedRequest : BaseRequest
{
    // Automatic authorization enforcement
}

// Full pipeline requests (all features enabled)
public abstract class FullPipelineRequest : CacheableRequest, ITransactional, IAuditable
{
    public virtual string GetAuditDetails() => GetType().Name;
}
```

### Custom Attributes

Extend the framework with custom attributes:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public class RateLimitAttribute : Attribute
{
    public int RequestsPerMinute { get; set; }
    public string? PolicyName { get; set; }
}

// Usage
[RateLimit(RequestsPerMinute = 10, PolicyName = "ProductApi")]
public class GetProductQuery : BaseRequest
{
    // Query implementation
}
```

## 🚀 Performance Considerations

- **Handler Caching**: Handlers are automatically cached after first discovery
- **Scoped Services**: Handlers are resolved as scoped services for optimal performance
- **Async by Default**: All operations are asynchronous with proper cancellation support
- **Memory Caching**: Built-in memory caching with configurable eviction policies
- **Pipeline Optimization**: Short-circuit caching to avoid unnecessary handler execution

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guidelines](CONTRIBUTING.md) for details.

### Development Setup

1. Clone the repository
2. Ensure you have .NET 9.0 SDK installed
3. Run `dotnet restore` to restore packages
4. Run `dotnet build` to build the solution
5. Run tests with `dotnet test`

### Code Style

- Follow standard C# naming conventions
- Use `async`/`await` for all asynchronous operations
- Include XML documentation for public APIs
- Write unit tests for new features

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🆘 Support

- 📚 [Documentation](https://github.com/can-acar/Conductor/wiki)
- 🐛 [Issue Tracker](https://github.com/can-acar/Conductor/issues)
- 💬 [Discussions](https://github.com/can-acar/Conductor/discussions)

## 🏆 Acknowledgments

- Inspired by MediatR and similar mediator pattern implementations
- Built with modern .NET features and best practices
- Designed for enterprise-grade applications

---

**Built with ❤️ for the .NET community**