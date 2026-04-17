using System.Diagnostics;

namespace IotDashboard.Api.Middleware;

/// <summary>
/// Middleware that:
///   1. Injects a Correlation-ID header into every request (X-Correlation-Id)
///   2. Logs request start + completion with method, path, status, and duration
///
/// Correlation IDs allow tracing a single request across backend logs,
/// Grafana dashboards, and browser dev tools — essential for debugging
/// real-time issues in production.
///
/// Register BEFORE GlobalExceptionMiddleware so exceptions also carry
/// the correlation ID in their log entries.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Use existing header if provided (e.g. from API gateway), else generate new
        var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                         ?? Guid.NewGuid().ToString("N")[..12];   // short 12-char ID

        ctx.Response.Headers["X-Correlation-Id"] = correlationId;

        // Make available to Serilog enrichers via LogContext
        using var _ = Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId);

        var sw = Stopwatch.StartNew();

        // Skip verbose logging for health check and SignalR negotiate
        bool isNoisy = ctx.Request.Path.StartsWithSegments("/health") ||
                       ctx.Request.Path.StartsWithSegments("/hubs");

        if (!isNoisy)
            _logger.LogInformation("{Method} {Path} started cid={CorrelationId}",
                ctx.Request.Method, ctx.Request.Path.Value, correlationId);

        await _next(ctx);

        sw.Stop();

        if (!isNoisy)
            _logger.LogInformation("{Method} {Path} → {Status} in {Ms}ms cid={CorrelationId}",
                ctx.Request.Method, ctx.Request.Path.Value,
                ctx.Response.StatusCode, sw.ElapsedMilliseconds, correlationId);
    }
}
