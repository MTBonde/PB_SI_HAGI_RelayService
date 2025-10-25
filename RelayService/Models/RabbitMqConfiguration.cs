namespace RelayService.Configuration;

/// <summary>
/// Configuration settings for RabbitMQ connection, exchanges, and consumer behavior
/// </summary>
public class RabbitMqConfiguration
{
    /// <summary>
    /// Gets or sets the RabbitMQ server hostname
    /// </summary>
    public string HostName { get; set; } = "rabbitmq";

    /// <summary>
    /// Gets or sets the RabbitMQ server port
    /// </summary>
    /// <remarks>Default AMQP port is 5672</remarks>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Gets or sets the maximum number of connection retry attempts before giving up
    /// </summary>
    public int MaxRetries { get; set; } = 10;

    /// <summary>
    /// Gets or sets the delay in milliseconds between connection retry attempts
    /// </summary>
    public int RetryDelayMilliseconds { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the name of the fanout exchange for global chat messages
    /// </summary>
    public string ChatGlobalExchange { get; set; } = "relay.chat.global";

    /// <summary>
    /// Gets or sets the name of the topic exchange for game events
    /// </summary>
    public string GameEventsExchange { get; set; } = "relay.game.events";

    /// <summary>
    /// Gets or sets the name of the topic exchange for session events
    /// </summary>
    public string SessionEventsExchange { get; set; } = "relay.session.events";

    /// <summary>
    /// Gets or sets the routing key wildcard pattern for topic exchanges
    /// </summary>
    /// <remarks>The '#' wildcard matches zero or more words in topic routing</remarks>
    public string TopicWildcard { get; set; } = "#";
}
