# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Conductor** is a .NET 9.0 request/response orchestration framework that provides attribute-driven middleware behaviors (validation, caching, transactions, auditing, authorization) and supports CQRS, event publishing, pipeline processing, and saga orchestration patterns.

**Solution Structure:**
- `Conductor/` — Core class library (transport-agnostic orchestration framework)
- `Conductor.Test/` — xUnit test project with Moq, FluentAssertions
- `ExampleWebApplication/` — ASP.NET Core web application demonstrating framework usage

## Build & Test Commands

**Build:**
```bash
dotnet build Conductor.sln
```

**Run Tests:**
```bash
dotnet test Conductor.Test/Conductor.Test.csproj
```

**Run Single Test:**
```bash
dotnet test Conductor.Test/Conductor.Test.csproj --filter "FullyQualifiedName~TestMethodName"
```

**Run Example Application:**
```bash
dotnet run --project ExampleWebApplication/ExampleWebApplication.csproj
```

## Core Architecture

### Request Routing & Handler Discovery

The central orchestrator is `ConductorService` (Conductor/Services/ConductorService.cs), which:

1. **Handler Discovery**: Scans assemblies for classes with methods decorated with `[Handle]` attribute. Handlers are resolved via DI (`IServiceProvider`/`IServiceScopeFactory`). Handler methods must accept a `BaseRequest`-derived type as first parameter and optionally `CancellationToken` as second parameter.

2. **Execution Flow**:
   - New pipeline approach (preferred): Uses `PipelineExecutor` with `IPipelineBehavior<TRequest, TResponse>` behaviors
   - Legacy approach (fallback): Direct execution with attribute-driven middleware (`[Validate]`, `[CacheModule]`, `[Transaction]`, `[Audit]`)

3. **Dual Execution Paths**:
   - If `IPipelineExecutor` is registered in DI → uses modern pipeline behaviors
   - Otherwise → falls back to legacy attribute-based execution (see `ExecuteLegacy` method in ConductorService.cs:66)

### Pipeline Behaviors

Modern pipeline behaviors are in `Conductor/Pipeline/BuiltInBehaviors.cs`:

- `LoggingBehavior<TRequest, TResponse>` — Request/response logging with correlation IDs
- `ValidationBehavior<TRequest, TResponse>` — Fluent validation integration
- `PerformanceBehavior<TRequest, TResponse>` — Execution time monitoring with configurable thresholds
- `CachingBehavior<TRequest, TResponse>` — Response caching for requests implementing `ICacheableRequest`
- `TransactionBehavior<TRequest, TResponse>` — Database transaction management for `ITransactionalRequest`
- `AuthorizationBehavior<TRequest, TResponse>` — Permission checks for `IAuthorizedRequest`
- `AuditingBehavior<TRequest, TResponse>` — Audit logging for `IAuditableRequest`

These behaviors execute in registration order and follow the decorator/chain-of-responsibility pattern. See `PipelineExecutor.cs:54-66` for pipeline construction.

### Attribute-Driven Configuration

Attributes in `Conductor/Attributes/`:

- `[Handle]` — Marks handler methods (required for discovery)
- `[Validate]` — Triggers request/response validation
- `[CacheModule]` — Configures response caching (key generation, duration, sliding expiration)
- `[Transaction]` — Wraps execution in database transaction
- `[Audit]` — Enables audit logging (request/response data, category, level)
- `[Pipeline("name")]` — Marks pipeline handler methods with execution order
- `[Saga]` — Marks event handler methods for saga orchestration

Attributes are processed in ConductorService.cs during the legacy execution path. Request types can implement marker interfaces (e.g., `ICacheableRequest`, `ITransactionalRequest`) to enable corresponding pipeline behaviors.

### Request/Response Patterns

**Base Request:** All requests inherit from `BaseRequest` (Conductor/Core/BaseRequest.cs), which provides a `Metadata` dictionary for cross-cutting concerns.

**Marker Interfaces:**
- `ICacheableRequest` — Implement `GetCacheKey()` and `GetCacheDuration()`
- `ITransactionalRequest` — Implement `RequiresTransaction` property
- `IAuthorizedRequest` — Implement `GetRequiredPermissions()`
- `IAuditableRequest` — Implement `GetAuditDetails()`

**Queries vs Commands:**
- Queries typically implement `ICacheableRequest` and use `[Cacheable]` attribute
- Commands typically implement `ITransactionalRequest`, `IAuthorizedRequest`, and `IAuditableRequest`

See `Conductor/Examples/ComprehensiveExample.cs` for canonical usage patterns.

### Dependency Injection Setup

**Registration:** Use `services.AddConductor(options => { ... })` extension method (Conductor/Extensions/ConductorExtensions.cs:15-42).

The framework automatically:
1. Registers `IConductor` → `ConductorService`
2. Registers cache and pipeline modules
3. Scans specified assemblies in `options.HandlerAssemblies` for handlers and validators
4. Auto-registers types with `[Handle]`, `[Saga]`, or `[Pipeline]` methods as scoped services
5. Auto-registers classes implementing `IValidator<T>` interface

**Handler Registration:** Handlers are discovered and registered automatically if their assembly is added to `options.HandlerAssemblies`. Ensure handler classes are registered in DI container.

**Pipeline Registration:** Use `services.AddConductorPipeline()` to register pipeline behaviors. Individual behaviors can be added with dedicated extension methods (e.g., `AddAuthorizationBehavior()`, `AddAuditingBehavior()`).

### Validation

The framework supports three validation strategies (checked in order):

1. **Auto-registered validators**: Classes implementing `IValidator<TRequest>` are auto-discovered and registered during `AddConductor()` setup. ConductorService checks for registered validators at runtime (see ConductorService.cs:666-688).

2. **Custom validators**: Specify via `[Validate(ValidatorType = "Fully.Qualified.TypeName")]` attribute

3. **DataAnnotations**: Fallback validation using `System.ComponentModel.DataAnnotations` attributes

See `Conductor/Validation/FluentValidation.cs` for `AbstractValidator<T>` base class and `Conductor/Validation/PropertyValidators.cs` for reusable property validators.

### Event Publishing & Saga Orchestration

**Events:** Publish events via `conductor.Publish<T>(Event<T>)`. Event handlers are methods decorated with `[Saga]` attribute that accept `Event<T>` or `IEvent` parameters.

**Sagas:** Long-running business processes with compensation logic. Key components:
- `ISagaOrchestrator<TState>` — Executes saga steps with automatic compensation on failure
- `ISagaPersistence<TState>` — Persists saga state (in-memory or durable storage)
- `ISagaMonitor` — Health monitoring and diagnostics
- `SagaTimeoutManager` — Background service for timeout handling

Saga steps execute sequentially and can return `SagaStepResult.Success(nextStep)`, `SagaStepResult.Compensate(reason)`, or `SagaStepResult.Complete()`. Compensation steps run in reverse order on failure.

### Correlation ID Handling

Correlation IDs are managed via:
- `ICorrelationIdHelper` interface (Conductor/Interfaces/ICorrelationIdHelper.cs)
- `CorrelationIdHelper` implementation (Conductor/Core/CorrelationIdHelper.cs)
- HTTP header `X-Correlation-ID` is checked; if absent, a new GUID is generated
- Stored in `BaseRequest.Metadata["CorrelationId"]` and `PipelineContext.CorrelationId`

## Key Files Reference

**Core Logic:**
- `Conductor/Services/ConductorService.cs` — Central request router (handler discovery, caching, validation, transactions, auditing, pipeline fallback)
- `Conductor/Pipeline/PipelineExecutor.cs` — Modern pipeline behavior execution
- `Conductor/Pipeline/BuiltInBehaviors.cs` — Default pipeline behaviors (canonical examples for pipeline handlers and service interfaces)

**Configuration:**
- `Conductor/Extensions/ConductorExtensions.cs` — DI registration and auto-discovery
- `Conductor/Extensions/ConductorOptions.cs` — Configuration options

**Examples:**
- `Conductor/Examples/ComprehensiveExample.cs` — Complete usage patterns (queries, commands, sagas, validation)
- `ExampleWebApplication/Program.cs` — Application startup configuration

## Development Conventions

1. **Attribute-Driven Behavior**: When adding new cross-cutting concerns, prefer attribute-based approach to maintain consistency with existing patterns (`[Handle]`, `[Validate]`, `[Audit]`, etc.).

2. **Handler Semantics**: Handler methods must:
   - Accept `BaseRequest`-derived type as first parameter
   - Optionally accept `CancellationToken` as second parameter
   - Return `Task<TResponse>` or `TResponse`
   - Be decorated with `[Handle]` attribute

3. **DI Scoping**: Handlers and validators are registered as scoped services. Pipeline behaviors are typically scoped to match handler lifetime. Services like `ICacheModule`, `IPipelineModule`, and `IAuditLogger` are singletons.

4. **Testing**: When adding behavior changes, create tests in `Conductor.Test/` that exercise:
   - Handler discovery
   - Attribute-driven validation
   - Caching behavior
   - Pipeline execution order
   - Compensation logic (for sagas)

5. **Error Handling**: Exceptions bubble up through the pipeline. `ConductorService` captures exceptions for audit logging (see `ExecuteLegacy` catch block at ConductorService.cs:178-192). Pipeline behaviors should re-throw exceptions after logging.

6. **Service Interfaces**: When implementing new pipeline behaviors, use the service interfaces defined in `BuiltInBehaviors.cs` as canonical examples:
   - `ICacheService` — Caching abstraction
   - `ITransactionService` — Transaction management
   - `IAuthorizationService` — Authorization checks
   - `IAuditService` — Audit logging

## Example Application

The `ExampleWebApplication/` demonstrates:
- Conductor setup in `Program.cs`
- Handler implementations in `Handlers/`
- Validators in `Validators/`
- Controller integration in `Controllers/`
- Pipeline steps in `Handlers/ProductPipelineSteps.cs`
- Event handlers in `Handlers/ProductEventHandlers.cs`

Run with Swagger UI at `/swagger` for API documentation.

## Important Notes

- **Public API Stability**: Minimize changes to public API surface (`IConductor`, `BaseRequest`, attribute constructors)
- **Assembly Scanning**: Handler discovery scans `ExecutingAssembly`, `EntryAssembly`, and `CallingAssembly`. If handlers aren't found, verify assembly is registered in `options.HandlerAssemblies`.
- **Pipeline vs Legacy**: The framework supports both modern pipeline behaviors and legacy attribute-based execution. When `IPipelineExecutor` is registered, it takes precedence (see ConductorService.cs:52-56).
- **Validator Auto-Registration**: Validators implementing `IValidator<T>` are auto-discovered and registered during `AddConductor()`. Manual registration is not required.
