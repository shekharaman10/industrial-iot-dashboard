using System.Net;
using System.Text.Json;

namespace IotDashboard.Api.Middleware;

/// <summary>
/// Catches unhandled exceptions and returns RFC 7807 Problem Details responses.
/// Prevents stack traces leaking to clients in production.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly RequestDelegate            _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                ctx.Request.Method, ctx.Request.Path);

            await WriteProblemAsync(ctx, ex);
        }
    }

    private static Task WriteProblemAsync(HttpContext ctx, Exception ex)
    {
        ctx.Response.StatusCode  = ex is ArgumentException
            ? (int)HttpStatusCode.BadRequest
            : (int)HttpStatusCode.InternalServerError;

        ctx.Response.ContentType = "application/problem+json";

        bool isDev = ctx.RequestServices
            .GetRequiredService<IWebHostEnvironment>()
            .IsDevelopment();

        var problem = new
        {
            type     = "https://tools.ietf.org/html/rfc7807",
            title    = ex.GetType().Name,
            status   = ctx.Response.StatusCode,
            detail   = isDev ? ex.Message    : "An internal error occurred.",
            trace    = isDev ? ex.StackTrace : null,
        };

        return ctx.Response.WriteAsync(JsonSerializer.Serialize(problem, _jsonOpts));
    }
}
