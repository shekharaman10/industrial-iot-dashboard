using System.Text;
using System.Threading.Channels;
using IotDashboard.Api.Hubs;
using IotDashboard.Api.Middleware;
using IotDashboard.Application.Interfaces;
using IotDashboard.Application.Models;
using IotDashboard.Application.Services;
using IotDashboard.Domain.Entities;
using IotDashboard.Infrastructure.HealthChecks;
using IotDashboard.Infrastructure.Logging;
using IotDashboard.Infrastructure.Messaging;
using IotDashboard.Infrastructure.Persistence.InfluxDb;
using IotDashboard.Infrastructure.Persistence.Postgres;
using IotDashboard.Worker;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ─── Serilog ──────────────────────────────────────────────────────────────────
builder.AddSerilog();

// ─── Config ───────────────────────────────────────────────────────────────────
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));
builder.Services.Configure<InfluxDbOptions>(builder.Configuration.GetSection("InfluxDb"));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Postgres"));
builder.Services.Configure<AnalyticsEngineOptions>(
    builder.Configuration.GetSection(AnalyticsEngineOptions.Section));

// ─── Channel 1: MQTT raw messages → IngestionWorker ───────────────────────────
var mqttChannel = Channel.CreateBounded<TelemetryMessage>(new BoundedChannelOptions(2000)
{
    FullMode     = BoundedChannelFullMode.Wait,
    SingleReader = true,
    SingleWriter = false,
});
builder.Services.AddSingleton(mqttChannel.Writer);
builder.Services.AddSingleton(mqttChannel.Reader);

// ─── Channel 2: IngestionWorker → ProcessingWorker ────────────────────────────
var processingChannel = Channel.CreateBounded<SensorReading>(new BoundedChannelOptions(2000)
{
    FullMode     = BoundedChannelFullMode.Wait,
    SingleReader = true,
    SingleWriter = true,
});
builder.Services.AddSingleton(processingChannel.Writer);
builder.Services.AddSingleton(processingChannel.Reader);

// ─── Application Services ─────────────────────────────────────────────────────
builder.Services.AddSingleton<IAnalyticsEngine, AnalyticsEngine>();
builder.Services.AddSingleton<AlertService>();
builder.Services.AddSingleton<DeviceService>();

// ─── Infrastructure ───────────────────────────────────────────────────────────
builder.Services.AddSingleton<MqttMessageParser>();
builder.Services.AddSingleton<ISensorRepository, InfluxDbSensorRepository>();
builder.Services.AddSingleton<IAlertRepository,  PostgresAlertRepository>();
builder.Services.AddSingleton<IDeviceRepository, DeviceRepository>();

// ─── Background Workers ───────────────────────────────────────────────────────
builder.Services.AddHostedService<MqttSubscriberService>();
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<ProcessingWorker>();

// ─── JWT Authentication ───────────────────────────────────────────────────────
var jwtSecret   = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is required. Set it in .env or appsettings.");
var jwtIssuer   = builder.Configuration["Jwt:Issuer"]   ?? "iot-dashboard";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "iot-dashboard-clients";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtIssuer,
            ValidAudience            = jwtAudience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew                = TimeSpan.FromSeconds(30),
        };
        // SignalR sends the token as query string ?access_token=…
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ─── API + Swagger ────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "Industrial IoT Dashboard API",
        Version     = "v1",
        Description = "REST endpoints for sensor history, devices, and alert management.",
    });
    // Add JWT bearer input to Swagger UI
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = Microsoft.OpenApi.Models.ParameterLocation.Header,
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ─── SignalR ──────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(o =>
{
    o.EnableDetailedErrors  = builder.Environment.IsDevelopment();
    o.KeepAliveInterval     = TimeSpan.FromSeconds(15);
    o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// ─── CORS ─────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000", "http://localhost:5173"];

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins)
         .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
         .WithHeaders("content-type", "authorization", "x-requested-with")
         .AllowCredentials()));

// ─── Health Checks ────────────────────────────────────────────────────────────
var influxUrl  = builder.Configuration["InfluxDb:Url"] ?? "http://localhost:8086";
var pgConnStr  = builder.Configuration["Postgres:ConnectionString"] ?? "";

builder.Services.AddHealthChecks()
    .Add(new HealthCheckRegistration(
        name: "influxdb",
        factory: _ => new InfluxHealthCheck(influxUrl),
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]))
    .Add(new HealthCheckRegistration(
        name: "postgres",
        factory: _ => new PostgresHealthCheck(pgConnStr),
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]))
    .AddCheck<ChannelHealthCheck>(
        name: "channel-pipeline",
        tags: ["ready"]);

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Industrial IoT API v1"));
}

app.UseCors();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<SensorHub>("/hubs/sensors").RequireAuthorization();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = c => c.Tags.Contains("ready"),
    });
app.MapGet("/", () => app.Environment.IsDevelopment()
    ? Results.Redirect("/swagger")
    : Results.NotFound());

app.Run();

// ─── Inline health checks ─────────────────────────────────────────────────────
internal sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly string _connStr;
    public PostgresHealthCheck(string connStr) => _connStr = connStr;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(_connStr);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"PostgreSQL unreachable: {ex.Message}");
        }
    }
}
