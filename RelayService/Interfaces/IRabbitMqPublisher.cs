namespace RelayService.Interfaces;

/// <summary>
/// Interface for publishing messages to RabbitMQ exchanges
/// </summary>
public interface IRabbitMqPublisher
{
    /// <summary>
    /// Publishes a message to the specified RabbitMQ exchange
    /// </summary>
    /// <param name="exchange">The name of the exchange to publish to</param>
    /// <param name="routingKey">The routing key for the message (use empty string for fanout exchanges)</param>
    /// <param name="message">The message content as a string</param>
    Task PublishAsync(string exchange, string routingKey, string message);
}
