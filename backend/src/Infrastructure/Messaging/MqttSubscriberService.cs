using System.Text;
using System.Threading.Channels;
using IotDashboard.Application.Models;
using IotDashboard.Shared.Constants;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace IotDashboard.Infrastructure.Messaging;

/// <summary>
/// Background service responsible for MQTT broker connectivity only.
///
/// Responsibilities:
///   ✓ Connect to broker with Last-Will-Testament
///   ✓ Subscribe to all device telemetry topics (wildcard)
///   ✓ Non-blocking reconnect with exponential back-off
///   ✓ Write raw parsed messages to the bounded Channel (backpressure)
///
/// NOT responsible for:
///   ✗ Business logic
///   ✗ Analytics
///   ✗ Database writes
///   ✗ SignalR broadcasting
///
/// These are handled by IngestionWorker and ProcessingWorker consuming the Channel.
/// </summary>
public sealed class MqttSubscriberService : BackgroundService
{
    private readonly ILogger<MqttSubscriberService> _logger;
    private readonly MqttOptions                    _opts;
    private readonly MqttMessageParser              _parser;
    private readonly ChannelWriter<TelemetryMessage> _writer;

    private IMqttClient? _client;
    private int          _reconnectAttempts = 0;

    public MqttSubscriberService(
        ILogger<MqttSubscriberService> logger,
        IOptions<MqttOptions> opts,
        MqttMessageParser parser,
        ChannelWriter<TelemetryMessage> writer)
    {
        _logger = logger;
        _opts   = opts.Value;
        _parser = parser;
        _writer = writer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[MQTT] Subscriber service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _reconnectAttempts++;
                // Exponential back-off: 2s, 4s, 8s … capped at 60s
                int delay = Math.Min((int)Math.Pow(2, _reconnectAttempts) * 1000, 60_000);
                _logger.LogError(ex,
                    "[MQTT] Session ended. Attempt #{Attempt} — retrying in {Delay}ms",
                    _reconnectAttempts, delay);

                await Task.Delay(delay, stoppingToken);
            }
        }

        await DisconnectAsync();
        _logger.LogInformation("[MQTT] Subscriber service stopped");
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_opts.Host, _opts.Port)
            .WithClientId($"iot-backend-{Environment.MachineName}")
            .WithCleanSession(false)   // Retain QoS-1 queue across brief disconnects
            .WithWillTopic(Topics.BackendStatus)
            .WithWillPayload("{\"status\":\"offline\",\"source\":\"backend\"}")
            .WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .Build();

        _client.ApplicationMessageReceivedAsync += HandleMessageAsync;

        _client.DisconnectedAsync += async e =>
        {
            _logger.LogWarning("[MQTT] Disconnected: {Reason}", e.ReasonString);
            await Task.CompletedTask;
        };

        await _client.ConnectAsync(options, ct);
        _reconnectAttempts = 0;

        _logger.LogInformation("[MQTT] Connected to {Host}:{Port}", _opts.Host, _opts.Port);

        // Subscribe with wildcard to catch all current and future devices
        await _client.SubscribeAsync(
            new MqttTopicFilterBuilder()
                .WithTopic(Topics.TelemetryPattern)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(), ct);

        _logger.LogInformation("[MQTT] Subscribed to: {Pattern}", Topics.TelemetryPattern);

        // Announce backend is alive
        await _client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(Topics.BackendStatus)
                .WithPayload("{\"status\":\"online\",\"source\":\"backend\"}")
                .WithRetainFlag(true)
                .Build(), ct);

        // Block until session fails or cancellation requested
        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic   = args.ApplicationMessage.Topic;
        var payload = args.ApplicationMessage.PayloadSegment;

        var msg = _parser.Parse(payload, topic);
        if (msg is null) return;

        try
        {
            // WriteAsync blocks if channel is full (backpressure)
            // This is intentional: slow consumers should throttle the producer
            await _writer.WriteAsync(msg);
        }
        catch (ChannelClosedException)
        {
            _logger.LogWarning("[MQTT] Channel closed — message dropped for {Device}", msg.DeviceId);
        }
    }

    private async Task DisconnectAsync()
    {
        if (_client?.IsConnected == true)
        {
            try { await _client.DisconnectAsync(); }
            catch { /* best-effort */ }
        }
        _client?.Dispose();
    }
}

public sealed class MqttOptions
{
    public string Host                  { get; set; } = "localhost";
    public int    Port                  { get; set; } = 1883;
    public int    ReconnectDelaySeconds { get; set; } = 5;
}
