namespace IotDashboard.Application.Interfaces;

/// <summary>
/// Abstraction over System.Threading.Channel for internal pub/sub.
///
/// Current implementation: in-process bounded Channel (no external dependency).
/// Future: swap to Kafka consumer group for multi-replica horizontal scaling.
/// The swap requires only changing the DI registration in Program.cs — no
/// business logic changes.
///
/// BACKPRESSURE CONTRACT:
///   PublishAsync BLOCKS when the channel is at capacity (FullMode=Wait).
///   This is intentional — it propagates backpressure upstream to the
///   MQTT subscriber, which causes the TCP receive window to fill,
///   which causes the broker to slow QoS-1 message delivery.
///   Never use FullMode=Drop for sensor data — dropped readings are lost.
///
/// NOTE: This interface is not used directly by the current Worker classes,
///   which accept Channel&lt;T&gt; directly for performance.
///   This interface exists for future DI abstraction and testability.
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publish a message. Blocks if the channel is at capacity (backpressure).
    /// </summary>
    ValueTask PublishAsync<T>(T message, CancellationToken ct = default)
        where T : class;

    /// <summary>
    /// Subscribe to messages of type T. Returns an async stream
    /// that yields items as they are published, until cancellation.
    /// </summary>
    IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken ct = default)
        where T : class;
}
