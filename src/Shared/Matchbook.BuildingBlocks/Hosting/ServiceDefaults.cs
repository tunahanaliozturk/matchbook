using System.Text.Json.Serialization;
using MassTransit.Logging;
using MassTransit.Monitoring;
using Matchbook.BuildingBlocks.Http;
using Matchbook.BuildingBlocks.Persistence;
using Matchbook.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Matchbook.BuildingBlocks.Hosting;

public static class ServiceDefaults
{
    /// <summary>
    /// What every Matchbook host needs before its own registrations: telemetry, authentication, problem details,
    /// health checks, JSON conventions and a clock.
    /// </summary>
    public static WebApplicationBuilder AddMatchbookDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddMatchbookAuthentication(builder.Configuration);
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ProblemMapping>();
        builder.Services.AddHealthChecks();
        builder.Services.AddOpenApi();
        builder.Services.Configure<JsonOptions>(static options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.AddMatchbookTelemetry(serviceName);
        return builder;
    }

    /// <summary>
    /// Traces, metrics and logs over OTLP when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set. Every service's own
    /// meter is named <c>Matchbook.{Service}</c> and picked up by the wildcard.
    /// </summary>
    public static WebApplicationBuilder AddMatchbookTelemetry(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(static logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        OpenTelemetryBuilder telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(static tracing => tracing
                .AddAspNetCoreInstrumentation(static options =>
                    options.Filter = static context => !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal))
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddSource(DiagnosticHeaders.DefaultListenerName))
            .WithMetrics(static metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(InstrumentationOptions.MeterName)
                .AddMeter("Matchbook.*"));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// The request pipeline every host shares. Liveness answers as long as the process runs; readiness only when
    /// the database and the broker answer, so a load balancer stops sending traffic before requests fail.
    /// </summary>
    public static WebApplication UseMatchbookDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains(HealthTags.Ready),
        }).AllowAnonymous();
        app.MapOpenApi().AllowAnonymous();

        return app;
    }
}
