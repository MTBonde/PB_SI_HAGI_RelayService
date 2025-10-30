using RelayService.Models;

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

    /// <summary>
    /// Publishes a chat message to the specified RabbitMQ exchange with automatic JSON serialization
    /// </summary>
    /// <param name="exchange">The name of the exchange to publish to</param>
    /// <param name="routingKey">The routing key for the message (use empty string for fanout exchanges)</param>
    /// <param name="chatMessage">The chat message object to serialize and publish</param>
    Task PublishChatMessageAsync(string exchange, string routingKey, ChatMessage chatMessage);
}
