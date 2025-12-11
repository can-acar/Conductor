using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;
using Conductor.Attributes;
using Conductor.Core;
using Conductor.Enums;
using Conductor.Interfaces;
using Conductor.Modules.Cache;
using Conductor.Modules.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ConductorValidationException = Conductor.Core.ValidationException;
using ConductorAttributes = Conductor.Attributes;
using ValidationResult = Conductor.Core.ValidationResult;

namespace Conductor.Services;

public class ConductorService : IConductor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ICacheModule _cacheModule;
    private readonly IPipelineModule _pipelineModule;
    private readonly ILogger<ConductorService> _logger;
    private readonly IAuditLogger _auditLogger;
    private readonly ConcurrentDictionary<Type, MethodInfo> _handlerCache = new();

    public ConductorService(
        IServiceProvider serviceProvider,
        IServiceScopeFactory serviceScopeFactory,
        ICacheModule cacheModule,
        IPipelineModule pipelineModule,
        ILogger<ConductorService> logger,
        IAuditLogger auditLogger)
    {
        _serviceProvider = serviceProvider;
        _serviceScopeFactory = serviceScopeFactory;
        _cacheModule = cacheModule;
        _pipelineModule = pipelineModule;
        _logger = logger;
        _auditLogger = auditLogger;
    }

    public async Task<TResponse> Send<TResponse>(BaseRequest request, CancellationToken cancellationToken = default)
    {
        var result = await Send(request, cancellationToken);
        return (TResponse)result;
    }

    public async Task<object> Send(BaseRequest request, CancellationToken cancellationToken = default)
    {
        // Use pipeline executor if available, otherwise fallback to legacy execution
        // Create scope to resolve scoped IPipelineExecutor service
        using var scope = _serviceScopeFactory.CreateScope();
        var pipelineExecutor = scope.ServiceProvider.GetService<IPipelineExecutor>();
        if (pipelineExecutor != null)
        {
            return await pipelineExecutor.ExecuteAsync<object>(request, cancellationToken);
        }

        return await ExecuteLegacy(request, cancellationToken);
    }

    public async Task<object> ExecuteHandlerAsync(BaseRequest request, CancellationToken cancellationToken = default)
    {
        return await ExecuteLegacy(request, cancellationToken);
    }

    private async Task<object> ExecuteLegacy(BaseRequest request, CancellationToken cancellationToken = default)
    {
        // Input validation
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var requestType = request.GetType();
        _logger.LogDebug("Processing request of type: {RequestType}", requestType.Name);

        // Find handler method with cancellation support
        var handlerInfo = await FindHandlerMethodAsync(requestType, cancellationToken).ConfigureAwait(false);
        if (handlerInfo == null)
        {
            var exception = new InvalidOperationException($"No handler found for request type: {requestType.Name}");
            _logger.LogError(exception, "Handler discovery failed for {RequestType}", requestType.Name);
            throw exception;
        }

        var (handlerType, method, cacheAttribute) = handlerInfo.Value;

        // Get all middleware attributes
        var validateAttribute = method.GetCustomAttribute<ValidateAttribute>();
        var transactionAttribute = method.GetCustomAttribute<TransactionAttribute>();
        var auditAttribute = method.GetCustomAttribute<AuditAttribute>();

        // Initialize audit entry
        AuditEntry? auditEntry = null;
        var stopwatch = Stopwatch.StartNew();

        if (auditAttribute != null)
        {
            auditEntry = new AuditEntry
            {
                HandlerType = handlerType.Name,
                HandlerMethod = method.Name,
                RequestType = requestType.Name,
                RequestData = auditAttribute.LogRequest ? request : null,
                Category = auditAttribute.Category,
                Level = auditAttribute.Level,
                CorrelationId = request.Metadata.TryGetValue("CorrelationId", out var corrId) ? corrId?.ToString() : null
            };
            AuditContext.Set(auditEntry);
        }

        try
        {
            // Validation phase
            if (validateAttribute is { ValidateRequest: true })
            {
                await ValidateRequest(request, validateAttribute, cancellationToken).ConfigureAwait(false);
            }

            // Check cache first
            if (cacheAttribute != null)
            {
                var cacheKey = GenerateCacheKey(request, cacheAttribute);
                var cachedResult = await _cacheModule.GetAsync<object>(cacheKey);
                if (cachedResult != null)
                {
                    _logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);

                    if (auditEntry != null)
                    {
                        auditEntry.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                        auditEntry.ResponseData = auditAttribute!.LogResponse ? cachedResult : null;
                        auditEntry.Metadata["CacheHit"] = true;
                        await _auditLogger.LogAsync(auditEntry, cancellationToken);
                    }

                    return cachedResult;
                }
            }

            // Execute handler within transaction if needed
            object result;
            if (transactionAttribute != null)
            {
                result = await ExecuteWithTransactionAsync(request, handlerType, method, transactionAttribute, cancellationToken);
            }
            else
            {
                // Execute handler within scope
                using var scope = _serviceScopeFactory.CreateScope();
                var handlerInstance = scope.ServiceProvider.GetRequiredService(handlerType);
                result = await ExecuteHandlerAsync(handlerInstance, method, request, cancellationToken);
            }

            // Validate response if needed
            if (validateAttribute != null && validateAttribute.ValidateResponse && result != null)
            {
                await ValidateResponse(result, validateAttribute);
            }

            // Cache result if needed
            if (cacheAttribute != null && result != null)
            {
                var cacheKey = GenerateCacheKey(request, cacheAttribute);
                var expiration = TimeSpan.FromSeconds(cacheAttribute.Duration);
                await _cacheModule.SetAsync(cacheKey, result, expiration, cacheAttribute.SlidingExpiration);
            }

            // Complete audit
            if (auditEntry != null)
            {
                auditEntry.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                auditEntry.ResponseData = auditAttribute!.LogResponse ? result : null;
                auditEntry.IsSuccess = true;
                await _auditLogger.LogAsync(auditEntry, cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            // Log audit error
            if (auditEntry != null)
            {
                auditEntry.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
                auditEntry.IsSuccess = false;
                auditEntry.ErrorMessage = ex.Message;
                auditEntry.StackTrace = ex.StackTrace;
                auditEntry.Level = AuditLevel.Error;
                await _auditLogger.LogAsync(auditEntry, cancellationToken);
            }

            throw;
        }
        finally
        {
            AuditContext.Clear();
            stopwatch.Stop();
        }
    }

    public async Task Publish<T>(Event<T> eventData, CancellationToken cancellationToken = default)
    {
        await PublishInternal(eventData, cancellationToken);
    }

    public async Task Publish(IEvent eventData, CancellationToken cancellationToken = default)
    {
        await PublishInternal(eventData, cancellationToken);
    }

    public async Task PublishAll(CancellationToken cancellationToken = default, params IEvent[] events)
    {
        var tasks = events.Select(e => PublishInternal(e, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task PublishInternal(IEvent eventData, CancellationToken cancellationToken = default)
    {
        var dataType = eventData.Data?.GetType();
        if (dataType == null) return;

        var handlers = await FindEventHandlers(dataType);
        var tasks = handlers.Select(async handler =>
        {
            try
            {
                await ExecuteEventHandler(handler, eventData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing event handler: {HandlerType} for event: {EventType}",
                    handler.GetType().Name, dataType.Name);
            }
        });

        await Task.WhenAll(tasks);
    }

    public async Task<TResponse> SendThrough<TResponse>(Bus<object> busData, CancellationToken cancellationToken = default)
    {
        return await SendThroughInternal<TResponse>(busData, cancellationToken);
    }

    public async Task<TResponse> SendThrough<TData, TResponse>(Bus<TData> busData, CancellationToken cancellationToken = default)
    {
        return await SendThroughInternal<TResponse>(busData, cancellationToken);
    }

    public async Task<object> SendThrough(IBus busData, CancellationToken cancellationToken = default)
    {
        return await SendThroughInternal<object>(busData, cancellationToken);
    }

    private async Task<TResponse> SendThroughInternal<TResponse>(IBus busData, CancellationToken cancellationToken = default)
    {
        var dataType = busData.Data?.GetType();
        if (dataType == null)
            throw new ArgumentException("Bus data cannot be null");

        var handlers = await FindPipelineHandlers(dataType);

        object currentData = busData.Data;
        foreach (var handler in handlers)
        {
            currentData = await ExecutePipelineHandler(handler, busData);
        }

        return (TResponse)currentData;
    }

    private async Task<(Type handlerType, MethodInfo method, CacheModuleAttribute? cacheAttr)?> FindHandlerMethodAsync(Type requestType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("Looking for handler for request type: {RequestType}", requestType.FullName);

        if (_handlerCache.TryGetValue(requestType, out var cachedMethod))
        {
            _logger.LogDebug("Found cached handler method: {MethodName} in {DeclaringType}", cachedMethod.Name, cachedMethod.DeclaringType?.Name);
            var cachedCacheAttr = cachedMethod.GetCustomAttribute<CacheModuleAttribute>();
            return (cachedMethod.DeclaringType!, cachedMethod, cachedCacheAttr);
        }

        // Scan assemblies for handler types with Handle attribute
        var currentAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
        var callingAssembly = System.Reflection.Assembly.GetCallingAssembly();

        var assemblies = new[] { currentAssembly, entryAssembly, callingAssembly }
            .Where(a => a != null)
            .Distinct()
            .ToArray();

        foreach (var assembly in assemblies)
        {
            try
            {
                _logger.LogDebug("Scanning assembly: {AssemblyName}", assembly.FullName);
                var handlerTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract)
                    .Where(t => t.GetMethods().Any(m => m.GetCustomAttribute<HandleAttribute>() != null));

                _logger.LogDebug("Found {Count} handler types in assembly {AssemblyName}", handlerTypes.Count(), assembly.GetName().Name);

                foreach (var handlerType in handlerTypes)
                {
                    try
                    {
                        _logger.LogDebug("Checking handler type: {HandlerType}", handlerType.FullName);

                        // Use scope to resolve scoped services
                        using var scope = _serviceScopeFactory.CreateScope();
                        var handler = scope.ServiceProvider.GetService(handlerType);
                        if (handler != null)
                        {
                            _logger.LogDebug("Handler instance resolved: {HandlerType}", handlerType.Name);
                            var methods = handlerType.GetMethods()
                                .Where(m => m.GetCustomAttribute<HandleAttribute>() != null);

                            foreach (var method in methods)
                            {
                                var parameters = method.GetParameters();
                                _logger.LogDebug("Checking method: {MethodName} with parameter type: {ParameterType}",
                                    method.Name, parameters.Length > 0 ? parameters[0].ParameterType.FullName : "none");

                                if (parameters.Length == 1)
                                {
                                    var parameterType = parameters[0].ParameterType;

                                    // Direct type match
                                    if (parameterType == requestType)
                                    {
                                        _logger.LogInformation("Found exact matching handler: {HandlerType}.{MethodName} for {RequestType}",
                                            handlerType.Name, method.Name, requestType.Name);
                                        _handlerCache[requestType] = method;
                                        var cacheAttr = method.GetCustomAttribute<CacheModuleAttribute>();
                                        return (handlerType, method, cacheAttr);
                                    }

                                    // Generic type match - check if both are generic and have same definition
                                    if (parameterType.IsGenericType && requestType.IsGenericType)
                                    {
                                        var paramGenericDef = parameterType.GetGenericTypeDefinition();
                                        var requestGenericDef = requestType.GetGenericTypeDefinition();

                                        if (paramGenericDef == requestGenericDef)
                                        {
                                            var paramGenericArgs = parameterType.GetGenericArguments();
                                            var requestGenericArgs = requestType.GetGenericArguments();

                                            if (paramGenericArgs.Length == requestGenericArgs.Length)
                                            {
                                                bool argsMatch = true;
                                                for (int i = 0; i < paramGenericArgs.Length; i++)
                                                {
                                                    if (paramGenericArgs[i] != requestGenericArgs[i])
                                                    {
                                                        argsMatch = false;
                                                        break;
                                                    }
                                                }

                                                if (argsMatch)
                                                {
                                                    _logger.LogInformation("Found generic matching handler: {HandlerType}.{MethodName} for {RequestType}",
                                                        handlerType.Name, method.Name, requestType.Name);
                                                    _handlerCache[requestType] = method;
                                                    var cacheAttr = method.GetCustomAttribute<CacheModuleAttribute>();
                                                    return (handlerType, method, cacheAttr);
                                                }
                                            }
                                        }
                                    }

                                    // Assignability check
                                    if (parameterType.IsAssignableFrom(requestType))
                                    {
                                        _logger.LogInformation("Found assignable matching handler: {HandlerType}.{MethodName} for {RequestType}",
                                            handlerType.Name, method.Name, requestType.Name);
                                        _handlerCache[requestType] = method;
                                        var cacheAttr = method.GetCustomAttribute<CacheModuleAttribute>();
                                        return (handlerType, method, cacheAttr);
                                    }
                                }
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Handler type {HandlerType} not registered in DI container", handlerType.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error resolving handler type: {HandlerType}", handlerType.FullName);
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning assembly: {AssemblyName}", assembly.FullName);
                continue;
            }
        }

        _logger.LogWarning("No handler found for request type: {RequestType}. Searched in {AssemblyCount} assemblies.",
            requestType.FullName, assemblies.Length);
        return null;
    }

    private object GetHandlerInstance(Type handlerType)
    {
        // Try to get from current scope first, fallback to creating new scope
        var handler = _serviceProvider.GetService(handlerType);
        if (handler != null)
            return handler;

        // Create scope for scoped services
        using var scope = _serviceScopeFactory.CreateScope();
        handler = scope.ServiceProvider.GetService(handlerType);

        return handler ?? throw new InvalidOperationException($"Handler not registered: {handlerType.Name}");
    }

    private async Task<object> ExecuteHandlerAsync(object handler, MethodInfo method, BaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Determine if method accepts CancellationToken
            var parameters = method.GetParameters();
            var methodAcceptsCancellation = parameters.Length > 1 && parameters[1].ParameterType == typeof(CancellationToken);

            object[] args = methodAcceptsCancellation
                ? [request, cancellationToken]
                : [request];

            var result = method.Invoke(handler, args);

            if (result is Task task)
            {
                // Handle both Task and Task<T>
                await task.ConfigureAwait(false);

                // Check if it's a generic Task<T>
                var taskType = task.GetType();
                if (taskType.IsGenericType)
                {
                    var property = taskType.GetProperty("Result");
                    return property?.GetValue(task) ?? new object();
                }

                // Non-generic Task (void async)
                return new object();
            }

            // Synchronous method
            return result ?? new object();
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            // Unwrap the inner exception for cleaner stack traces
            throw ex.InnerException;
        }
    }

    private string GenerateCacheKey(BaseRequest request, CacheModuleAttribute cacheAttribute)
    {
        if (!string.IsNullOrEmpty(cacheAttribute.CacheKey))
            return cacheAttribute.CacheKey;

        var requestJson = System.Text.Json.JsonSerializer.Serialize(request);
        var hash = requestJson.GetHashCode();
        return $"{request.GetType().Name}_{hash}";
    }

    private async Task<List<object>> FindEventHandlers(Type dataType)
    {
        var handlers = new List<object>();

        // Find all services that have methods with [Saga] attribute
        var services = _serviceProvider.GetServices<object>();

        foreach (var service in services)
        {
            var methods = service.GetType().GetMethods()
                .Where(m => m.GetCustomAttribute<SagaAttribute>() != null);

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 1)
                {
                    var paramType = parameters[0].ParameterType;

                    // Check if parameter is Event<T> where T matches our data type
                    if (paramType.IsGenericType &&
                        paramType.GetGenericTypeDefinition() == typeof(Event<>) &&
                        paramType.GetGenericArguments()[0].IsAssignableFrom(dataType))
                    {
                        handlers.Add(new { Handler = service, Method = method });
                    }
                    // Or check if parameter is IEvent
                    else if (typeof(IEvent).IsAssignableFrom(paramType))
                    {
                        handlers.Add(new { Handler = service, Method = method });
                    }
                }
            }
        }

        return handlers;
    }

    private async Task ExecuteEventHandler(object handlerInfo, IEvent eventData)
    {
        var info = handlerInfo as dynamic;
        var handler = info.Handler;
        var method = info.Method as MethodInfo;

        if (method == null) return;

        var parameters = method.GetParameters();
        if (parameters.Length == 1)
        {
            var paramType = parameters[0].ParameterType;

            object parameter;
            if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(Event<>))
            {
                // Create Event<T> from IEvent
                var dataType = paramType.GetGenericArguments()[0];
                var eventType = typeof(Event<>).MakeGenericType(dataType);
                parameter = Activator.CreateInstance(eventType, eventData.Data)!;

                // Copy properties
                var timestampProp = eventType.GetProperty("Timestamp");
                var eventIdProp = eventType.GetProperty("EventId");
                var metadataProp = eventType.GetProperty("Metadata");

                timestampProp?.SetValue(parameter, eventData.Timestamp);
                eventIdProp?.SetValue(parameter, eventData.EventId);
                metadataProp?.SetValue(parameter, eventData.Metadata);
            }
            else
            {
                parameter = eventData;
            }

            var result = method.Invoke(handler, new[] { parameter });

            if (result is Task task)
            {
                await task;
            }
        }
    }

    private async Task<List<object>> FindPipelineHandlers(Type dataType)
    {
        var handlers = new List<object>();

        // Find all services that have methods with [Pipeline] attribute
        var services = _serviceProvider.GetServices<object>();

        foreach (var service in services)
        {
            var methods = service.GetType().GetMethods()
                .Where(m => m.GetCustomAttribute<PipelineAttribute>() != null);

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 1)
                {
                    var paramType = parameters[0].ParameterType;

                    // Check if parameter is Bus<T> where T matches our data type
                    if (paramType.IsGenericType &&
                        paramType.GetGenericTypeDefinition() == typeof(Bus<>) &&
                        paramType.GetGenericArguments()[0].IsAssignableFrom(dataType))
                    {
                        var pipelineAttr = method.GetCustomAttribute<PipelineAttribute>()!;
                        handlers.Add(new
                        {
                            Handler = service,
                            Method = method,
                            Order = pipelineAttr.Order
                        });
                    }
                    // Or check if parameter is IBus
                    else if (typeof(IBus).IsAssignableFrom(paramType))
                    {
                        var pipelineAttr = method.GetCustomAttribute<PipelineAttribute>()!;
                        handlers.Add(new
                        {
                            Handler = service,
                            Method = method,
                            Order = pipelineAttr.Order
                        });
                    }
                }
            }
        }

        // Sort by order
        return handlers.OrderBy(h => ((dynamic)h).Order).ToList();
    }

    private async Task<object> ExecutePipelineHandler(object handlerInfo, IBus busData)
    {
        var info = handlerInfo as dynamic;
        var handler = info.Handler;
        var method = info.Method as MethodInfo;

        if (method == null) return busData.Data!;

        var parameters = method.GetParameters();
        if (parameters.Length == 1)
        {
            var paramType = parameters[0].ParameterType;

            object parameter;
            if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(Bus<>))
            {
                // Create Bus<T> from IBus
                var dataType = paramType.GetGenericArguments()[0];
                var busType = typeof(Bus<>).MakeGenericType(dataType);
                parameter = Activator.CreateInstance(busType, busData.Data)!;

                // Copy properties
                var correlationIdProp = busType.GetProperty("CorrelationId");
                var contextProp = busType.GetProperty("Context");

                correlationIdProp?.SetValue(parameter, busData.CorrelationId);
                contextProp?.SetValue(parameter, busData.Context);
            }
            else
            {
                parameter = busData;
            }

            var result = method.Invoke(handler, new[] { parameter });

            if (result is Task task)
            {
                await task;
                var property = task.GetType().GetProperty("Result");
                return property?.GetValue(task) ?? busData.Data!;
            }

            return result ?? busData.Data!;
        }

        return busData.Data!;
    }

    private async Task ValidateRequest(BaseRequest request, ValidateAttribute validateAttribute, CancellationToken cancellationToken = default)
    {
        try
        {
            // First try to find auto-registered validator for the request type
            var requestType = request.GetType();
            var validatorInterfaceType = typeof(IValidator<>).MakeGenericType(requestType);

            using var scope = _serviceScopeFactory.CreateScope();
            var autoValidator = scope.ServiceProvider.GetService(validatorInterfaceType);

            if (autoValidator != null)
            {
                _logger.LogDebug("Found auto-registered validator for type: {RequestType}", requestType.Name);
                var validateMethod = validatorInterfaceType.GetMethod("ValidateAsync");
                if (validateMethod != null)
                {
                    var validationTask = (Task<ValidationResult>)validateMethod.Invoke(autoValidator, [request, cancellationToken])!;
                    var validationResult = await validationTask;

                    if (!validationResult.IsValid && validateAttribute.ThrowOnValidationError)
                    {
                        throw new ConductorValidationException(validationResult);
                    }

                    return;
                }
            }

            // Use custom validator if specified
            if (!string.IsNullOrEmpty(validateAttribute.ValidatorType))
            {
                var validatorType = Type.GetType(validateAttribute.ValidatorType);
                if (validatorType != null)
                {
                    var validator = scope.ServiceProvider.GetService(validatorType);
                    if (validator != null)
                    {
                        var validateMethod = validatorType.GetMethod("ValidateAsync");
                        if (validateMethod != null)
                        {
                            var validationTask = (Task<ValidationResult>)validateMethod.Invoke(validator, new object[] { request, CancellationToken.None })!;
                            var validationResult = await validationTask;

                            if (!validationResult.IsValid && validateAttribute.ThrowOnValidationError)
                            {
                                throw new ConductorValidationException(validationResult);
                            }

                            return;
                        }
                    }
                }
            }

            // Fallback to DataAnnotations validation
            var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(request);
            var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

            if (!Validator.TryValidateObject(request, validationContext, validationResults, true))
            {
                if (validateAttribute.ThrowOnValidationError)
                {
                    var errors = validationResults.Select(vr => new ValidationError(
                        vr.MemberNames.FirstOrDefault() ?? "",
                        vr.ErrorMessage ?? "",
                        "DataAnnotation")).ToArray();

                    throw new ConductorValidationException(ValidationResult.Failure(errors));
                }
            }
        }
        catch (ConductorValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during request validation");
            if (validateAttribute.ThrowOnValidationError)
                throw;
        }
    }

    private async Task ValidateResponse(object response, ValidateAttribute validateAttribute)
    {
        try
        {
            // First try to find auto-registered validator for the response type
            var responseType = response.GetType();
            var validatorInterfaceType = typeof(IValidator<>).MakeGenericType(responseType);

            using var scope = _serviceScopeFactory.CreateScope();
            var autoValidator = scope.ServiceProvider.GetService(validatorInterfaceType);

            if (autoValidator != null)
            {
                _logger.LogDebug("Found auto-registered validator for response type: {ResponseType}", responseType.Name);
                var validateMethod = validatorInterfaceType.GetMethod("ValidateAsync");
                if (validateMethod != null)
                {
                    var validationTask = (Task<ValidationResult>)validateMethod.Invoke(autoValidator, new object[] { response, CancellationToken.None })!;
                    var validationResult = await validationTask;

                    if (!validationResult.IsValid && validateAttribute.ThrowOnValidationError)
                    {
                        throw new ConductorValidationException(validationResult);
                    }

                    return;
                }
            }

            // Use custom validator if specified
            if (!string.IsNullOrEmpty(validateAttribute.ValidatorType))
            {
                var validatorType = Type.GetType(validateAttribute.ValidatorType);
                if (validatorType != null)
                {
                    var validator = scope.ServiceProvider.GetService(validatorType);
                    if (validator != null)
                    {
                        var validateMethod = validatorType.GetMethod("ValidateAsync");
                        if (validateMethod != null)
                        {
                            var validationTask = (Task<ValidationResult>)validateMethod.Invoke(validator, new object[] { response, CancellationToken.None })!;
                            var validationResult = await validationTask;

                            if (!validationResult.IsValid && validateAttribute.ThrowOnValidationError)
                            {
                                throw new ConductorValidationException(validationResult);
                            }

                            return;
                        }
                    }
                }
            }

            // Fallback to DataAnnotations validation
            var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(response);
            var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

            if (!Validator.TryValidateObject(response, validationContext, validationResults, true))
            {
                if (validateAttribute.ThrowOnValidationError)
                {
                    var errors = validationResults.Select(vr => new ValidationError(
                        vr.MemberNames.FirstOrDefault() ?? "",
                        vr.ErrorMessage ?? "",
                        "DataAnnotation")).ToArray();

                    throw new ConductorValidationException(ValidationResult.Failure(errors));
                }
            }
        }
        catch (ConductorValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during response validation");
            if (validateAttribute.ThrowOnValidationError)
                throw;
        }
    }

    private async Task<object> ExecuteWithTransactionAsync(BaseRequest request, Type handlerType, MethodInfo method, TransactionAttribute transactionAttribute, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(transactionAttribute);
        cancellationToken.ThrowIfCancellationRequested();

        await using var transactionScope = new TransactionScope(transactionAttribute);
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var handlerInstance = scope.ServiceProvider.GetRequiredService(handlerType);
            var result = await ExecuteHandlerAsync(handlerInstance, method, request, cancellationToken);

            transactionScope.Complete();
            return result;
        }
        catch
        {
            // Transaction will rollback automatically
            throw;
        }
    }
}