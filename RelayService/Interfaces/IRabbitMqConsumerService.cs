namespace RelayService.Interfaces;

/// <summary>
/// Service for consuming messages from RabbitMQ exchanges and forwarding them to WebSocket clients
/// </summary>
public interface IRabbitMqConsumerService
{
    /// <summary>
    /// Starts the RabbitMQ consumer with connection retry logic and binds to configured exchanges
    /// </summary>
    /// <returns>A task that completes when the consumer is stopped or fails to connect after max retries</returns>
    /// <remarks>This method runs indefinitely and should be started in a background task</remarks>
    Task StartConsumerAsync();
}
