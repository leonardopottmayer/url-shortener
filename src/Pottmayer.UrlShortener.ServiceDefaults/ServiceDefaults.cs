using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pottmayer.Tars.Observability.AspNetCore.DI;
using Pottmayer.Tars.Observability.DI;
using Pottmayer.Tars.Observability.Options;
using Pottmayer.Tars.Observability.Serilog.DI;
using Serilog;

namespace Pottmayer.UrlShortener.ServiceDefaults;

/// <summary>
/// Shared observability composition for every url-shortener service (the Aspire "service defaults"
/// pattern). Traces + metrics over OTLP, Serilog logs (console + OTLP), and the correlation-id
/// middleware — so a single request can be followed Gateway -> Redirect -> Kafka -> Analytics.
/// </summary>
public static class ServiceDefaults
{
    public static IHostApplicationBuilder AddUrlShortenerObservability(this IHostApplicationBuilder builder)
    {
        builder.AddTarsObservabilityOptions();

        var options = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
                      ?? new ObservabilityOptions();
        var serviceName = string.IsNullOrWhiteSpace(options.ServiceName)
            ? builder.Environment.ApplicationName
            : options.ServiceName;

        builder.Services.AddTarsObservabilityResource(serviceName, options.ServiceVersion);

        // Tracing: pipeline -> instrumentation -> exporter.
        builder.Services.AddTarsTracing();
        builder.Services.AddTarsAspNetCoreTracing();
        builder.Services.AddTarsHttpClientTracing();
        builder.Services.AddTarsTracingOtlpExporter(options.OtlpEndpoint);

        // Metrics: pipeline -> instrumentation -> runtime -> exporter.
        builder.Services.AddTarsMetrics();
        builder.Services.AddTarsAspNetCoreMetrics();
        builder.Services.AddTarsHttpClientMetrics();
        builder.Services.AddTarsRuntimeMetrics();
        builder.Services.AddTarsMetricsOtlpExporter(options.OtlpEndpoint);

        // Logs via Serilog: console for local dev, OTLP for the collector.
        builder.Services.AddTarsSerilog(logger => logger
            .WriteTo.Console()
            .WriteToTarsOtlp(serviceName, options.OtlpEndpoint));

        return builder;
    }

    public static WebApplication UseUrlShortenerObservability(this WebApplication app)
    {
        app.UseTarsCorrelationId();
        return app;
    }
}
