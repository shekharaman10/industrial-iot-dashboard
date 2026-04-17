using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace IotDashboard.Infrastructure.Logging;

/// <summary>
/// Configures structured logging via Serilog.
/// Output sinks:
///   Development : Console (coloured, readable)
///   Production  : Console (JSON — parseable by Grafana Loki or any log aggregator)
///
/// Log levels:
///   Default         : Information
///   Microsoft.*     : Warning     (suppress ASP.NET noise)
///   MQTTnet.*       : Warning     (suppress MQTT heartbeat logs)
///   InfluxDB.*      : Warning
/// </summary>
public static class SerilogConfig
{
    public static WebApplicationBuilder AddSerilog(this WebApplicationBuilder builder)
    {
        bool isDev = builder.Environment.IsDevelopment();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft",                    LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime",  LogEventLevel.Information)
            .MinimumLevel.Override("MQTTnet",                     LogEventLevel.Warning)
            .MinimumLevel.Override("InfluxDB",                    LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application",  "IotDashboard")
            .Enrich.WithProperty("Environment",  builder.Environment.EnvironmentName)
            .WriteTo.Console(isDev
                ? new Serilog.Formatting.Display.MessageTemplateTextFormatter(
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                : new Serilog.Formatting.Json.JsonFormatter())
            .CreateLogger();

        builder.Host.UseSerilog();
        return builder;
    }
}
