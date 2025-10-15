<!-- Copilot instructions for Conductor repository -->
# Conductor — AI coding assistant instructions

This file gives focused, actionable guidance to an AI code assistant working on the Conductor codebase. Keep answers precise and make changes only when you can run and verify them.

- Project shape: core library in `Conductor/` (class library). Tests in `Conductor.Test/`. Example web app in `ExampleWebApplication/`.
- Key responsibilities:
  - `Conductor/Core/ConductorService.cs` — central request router: handler discovery, caching, validation, transactions, auditing, pipeline fallback.
  - `Conductor/Pipeline/BuiltInBehaviors.cs` — default pipeline behaviors (logging, validation, performance, caching, transactions, authorization, auditing). Use these as canonical examples for pipeline handlers and interfaces (ICacheService, ITransactionService, IAuthorizationService, IAuditService).
  - `Conductor/Attributes/*.cs` — attributes drive behavior (e.g. `[Handle]`, `[Validate]`, `[Pipeline]`, `[Audit]`, `[CacheModule]`, `[Saga]`). When adding or modifying behavior, prefer attribute-driven approach and keep attribute semantics consistent.

- Build & test:
  - The solution is a .NET solution. Use the provided VS Code tasks or run `dotnet build Conductor.sln` and `dotnet test Conductor.Test/Conductor.Test.csproj`.
  - When editing code, run the `build` task (task label: `build`) to verify compile.

- DI & handler discovery:
  - Handlers are normal classes whose methods are decorated with `[Handle]` (see `Conductor/Attributes/HandleAttribute.cs`). The service locates handlers by scanning assemblies and resolving via DI (`IServiceProvider` / `IServiceScopeFactory`). Ensure types are registered in DI when adding handlers.
  - Pipeline handlers use `[Pipeline("name")]` and are resolved from DI; order by `Order` property.

- Request/Response patterns:
  - Requests inherit `BaseRequest` and may implement marker interfaces (e.g., `ICacheableRequest`, `ITransactionalRequest`, `IAuditableRequest`, `IAuthorizedRequest`) used by pipeline behaviors.
  - Use attribute flags (e.g., `[Validate]`, `[Audit]`, `[CacheModule]`) on handler methods or classes to opt into behavior. See `Conductor/Core/ConductorService.cs` for exact runtime flow.

- Error handling & auditing:
  - Exceptions are allowed to bubble; `ConductorService` captures them for audit logging. If you change exception flow, update audit behavior in `Conductor/Attributes/AuditAttribute.cs` and `Conductor/Core/ConductorService.cs`.

- Patterns and conventions to follow when changing code:
  - Prefer small, focused changes. Keep public API surface stable.
  - Add tests in `Conductor.Test/` for any behavior changes. Tests should exercise: handler discovery, attribute-driven validation, caching, and pipeline execution.
  - When introducing new services/interfaces, register them in DI in examples or test setup. Handlers are expected to be resolved via DI scopes.

- Examples & reference files:
  - `Conductor/Examples/ComprehensiveExample.cs` — shows common usage patterns.
  - `Conductor/Core/ConductorService.cs` — primary logic to reference when implementing features.
  - `Conductor/Pipeline/BuiltInBehaviors.cs` — canonical pipeline implementations and helper interfaces.

- When to ask the human:
  - If a change requires altering public API (types, method signatures) or build infra. 
  - If a behavioral change affects audit, validation, caching, or transaction semantics that have cross-cutting impact.

Keep edits minimal, run `dotnet build` and `dotnet test` after changes, and include unit tests for new behavior. Ask for clarification only for high-level design decisions.
