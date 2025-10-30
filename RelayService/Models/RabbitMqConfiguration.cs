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

    // NOTE: RelayId removed for YAGNI principle (single relay instance only)
    // When scaling to multiple relay instances (k8s replicas), add back:
    // public string RelayId { get; set; } = Guid.NewGuid().ToString();
    // And update queue names to: relay.global.{relayId}

    /// <summary>
    /// Gets or sets the name of the direct exchange for private 1:1 chat messages
    /// </summary>
    /// <remarks>Messages are routed using routing key: user.{username}</remarks>
    public string ChatPrivateExchange { get; set; } = "chat.private";

    /// <summary>
    /// Gets or sets the name of the topic exchange for server-group messages
    /// </summary>
    /// <remarks>Messages are routed using routing key: server.{serverId}</remarks>
    public string ChatServerExchange { get; set; } = "chat.server";

    /// <summary>
    /// Gets or sets the name of the fanout exchange for global broadcast messages
    /// </summary>
    /// <remarks>All connected clients receive messages published to this exchange</remarks>
    public string ChatGlobalExchange { get; set; } = "chat.global";

    /// <summary>
    /// Gets or sets the routing key wildcard pattern for topic exchanges
    /// </summary>
    /// <remarks>The '#' wildcard matches zero or more words in topic routing</remarks>
    public string TopicWildcard { get; set; } = "#";
}
