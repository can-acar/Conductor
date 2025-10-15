using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Conductor.Transport.Http.Middleware;
using System.Text.Json;
using Conductor.Core;
using Conductor.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Conductor.Transport.Http.Extensions;

public static class HttpTransportExtensions
{
    public static IServiceCollection AddConductorHttpTransport(this IServiceCollection services)
    {
        return services.AddConductorHttpTransport(options => { });
    }

    public static IServiceCollection AddConductorHttpTransport(this IServiceCollection services, Action<ResponseFormattingOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Add HTTP context accessor
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        // Add transport services
        services.TryAddScoped<IResponseMetadataProvider, HttpResponseMetadataProvider>();
        services.TryAddScoped<HttpResponseFormatter>();
        services.TryAddScoped<IResponseFormatter<string>>(provider => provider.GetRequiredService<HttpResponseFormatter>());

        // Add JSON serializer options
        services.TryAddSingleton(provider =>
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                PropertyNameCaseInsensitive = true
            };
        });

        return services;
    }

    public static IServiceCollection AddConductorHttpTransport(this IServiceCollection services, Action<ResponseFormattingOptions> configureOptions, Action<JsonSerializerOptions> configureJson)
    {
        services.AddConductorHttpTransport(configureOptions);

        // Override JSON options
        services.AddSingleton(provider =>
        {
            var options = new JsonSerializerOptions();
            configureJson(options);
            return options;
        });

        return services;
    }

    public static IApplicationBuilder UseConductorHttpTransport(this IApplicationBuilder app)
    {
        // Add correlation ID middleware first
        app.UseMiddleware<CorrelationIdMiddleware>();

        // Add global exception handling middleware
        app.UseMiddleware<GlobalExceptionMiddleware>();

        // Add response formatting middleware
        app.UseMiddleware<ResponseFormatterMiddleware>();

        return app;
    }

    public static IApplicationBuilder UseConductorHttpTransport(this IApplicationBuilder app, Action<ResponseFormattingOptions> configureOptions)
    {
        // Configure options at runtime
        var serviceProvider = app.ApplicationServices;
        var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<ResponseFormattingOptions>>();
        var options = optionsMonitor.CurrentValue;
        configureOptions(options);

        return app.UseConductorHttpTransport();
    }

    // Extension methods for team standards
    public static IServiceCollection AddMinimalResponseFormatting(this IServiceCollection services)
    {
        return services.AddConductorHttpTransport(options =>
        {
            options.WrapAllResponses = false;
            options.IncludeTimestamp = false;
            options.IncludeCorrelationId = false;
            options.IncludeRequestId = false;
            options.SuccessMessage = "";
        });
    }

    public static IServiceCollection AddStandardResponseFormatting(this IServiceCollection services)
    {
        return services.AddConductorHttpTransport(options =>
        {
            options.WrapAllResponses = true;
            options.IncludeTimestamp = true;
            options.IncludeCorrelationId = true;
            options.IncludeRequestId = false;
            options.SuccessMessage = "Success";
        });
    }

    public static IServiceCollection AddEnterpriseResponseFormatting(this IServiceCollection services)
    {
        return services.AddConductorHttpTransport(options =>
        {
            options.WrapAllResponses = true;
            options.IncludeTimestamp = true;
            options.IncludeCorrelationId = true;
            options.IncludeRequestId = true;
            options.IncludeUserId = true;
            options.LogExceptions = true;
            options.SuccessMessage = "Operation completed successfully";
            options.GlobalMetadata.Add("ApiVersion", "v1");
            options.GlobalMetadata.Add("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown");
        });
    }

    public static IServiceCollection AddDevelopmentResponseFormatting(this IServiceCollection services)
    {
        return services.AddConductorHttpTransport(options =>
        {
            options.WrapAllResponses = true;
            options.IncludeTimestamp = true;
            options.IncludeCorrelationId = true;
            options.IncludeRequestId = true;
            options.IncludeStackTrace = true;
            options.LogExceptions = true;
            options.SuccessMessage = "Development Success";
        });
    }

    // Configuration helpers
    public static ResponseFormattingOptions ExcludePath(this ResponseFormattingOptions options, string path)
    {
        options.ExcludedPaths.Add(path);
        return options;
    }

    public static ResponseFormattingOptions ExcludeContentType(this ResponseFormattingOptions options, string contentType)
    {
        options.ExcludedContentTypes.Add(contentType);
        return options;
    }

    public static ResponseFormattingOptions AddGlobalMetadata(this ResponseFormattingOptions options, string key, object value)
    {
        options.GlobalMetadata[key] = value;
        return options;
    }

    public static ResponseFormattingOptions SetSuccessMessage(this ResponseFormattingOptions options, string message)
    {
        options.SuccessMessage = message;
        return options;
    }

    public static ResponseFormattingOptions EnableStackTrace(this ResponseFormattingOptions options, bool enable = true)
    {
        options.IncludeStackTrace = enable;
        return options;
    }
}

// Fluent configuration builder
public class ResponseFormattingBuilder
{
    private readonly ResponseFormattingOptions _options = new();
    private readonly IServiceCollection _services;

    internal ResponseFormattingBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public ResponseFormattingBuilder WrapResponses(bool wrap = true)
    {
        _options.WrapAllResponses = wrap;
        return this;
    }

    public ResponseFormattingBuilder IncludeTimestamp(bool include = true)
    {
        _options.IncludeTimestamp = include;
        return this;
    }

    public ResponseFormattingBuilder IncludeCorrelationId(bool include = true)
    {
        _options.IncludeCorrelationId = include;
        return this;
    }

    public ResponseFormattingBuilder IncludeRequestId(bool include = true)
    {
        _options.IncludeRequestId = include;
        return this;
    }

    public ResponseFormattingBuilder IncludeUserId(bool include = true)
    {
        _options.IncludeUserId = include;
        return this;
    }

    public ResponseFormattingBuilder WithSuccessMessage(string message)
    {
        _options.SuccessMessage = message;
        return this;
    }

    public ResponseFormattingBuilder WithErrorMessage(string message)
    {
        _options.DefaultErrorMessage = message;
        return this;
    }

    public ResponseFormattingBuilder ExcludePath(params string[] paths)
    {
        _options.ExcludedPaths.AddRange(paths);
        return this;
    }

    public ResponseFormattingBuilder ExcludeContentType(params string[] contentTypes)
    {
        _options.ExcludedContentTypes.AddRange(contentTypes);
        return this;
    }

    public ResponseFormattingBuilder AddGlobalMetadata(string key, object value)
    {
        _options.GlobalMetadata[key] = value;
        return this;
    }

    public ResponseFormattingBuilder EnableStackTrace(bool enable = true)
    {
        _options.IncludeStackTrace = enable;
        return this;
    }

    public ResponseFormattingBuilder LogExceptions(bool log = true)
    {
        _options.LogExceptions = log;
        return this;
    }

    public IServiceCollection Build()
    {
        _services.Configure<ResponseFormattingOptions>(options =>
        {
            options.WrapAllResponses = _options.WrapAllResponses;
            options.IncludeTimestamp = _options.IncludeTimestamp;
            options.IncludeCorrelationId = _options.IncludeCorrelationId;
            options.IncludeRequestId = _options.IncludeRequestId;
            options.IncludeUserId = _options.IncludeUserId;
            options.SuccessMessage = _options.SuccessMessage;
            options.DefaultErrorMessage = _options.DefaultErrorMessage;
            options.ExcludedPaths = _options.ExcludedPaths;
            options.ExcludedContentTypes = _options.ExcludedContentTypes;
            options.GlobalMetadata = _options.GlobalMetadata;
            options.IncludeStackTrace = _options.IncludeStackTrace;
            options.LogExceptions = _options.LogExceptions;
        });

        return _services.AddConductorHttpTransport();
    }
}

public static class ResponseFormattingBuilderExtensions
{
    public static ResponseFormattingBuilder ConfigureConductorResponseFormatting(this IServiceCollection services)
    {
        return new ResponseFormattingBuilder(services);
    }
}