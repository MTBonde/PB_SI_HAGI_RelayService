using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RelayService.Configuration;
using RelayService.Interfaces;

namespace RelayService.Services;

/// <summary>
/// Service for consuming messages from RabbitMQ exchanges and forwarding them to connected WebSocket clients
/// Handles connection management, exchange/queue setup, and message routing
/// </summary>
public class RabbitMqConsumerService : IRabbitMqConsumerService
{
    private readonly RabbitMqConfiguration configuration;
    private readonly IMessageBroadcaster messageBroadcaster;

    /// <summary>
    /// Initializes a new instance of the RabbitMqConsumerService class with the specified configuration and broadcaster
    /// </summary>
    /// <param name="configuration">RabbitMQ configuration containing connection and exchange settings</param>
    /// <param name="messageBroadcaster">Message broadcaster for forwarding messages to WebSocket clients</param>
    public RabbitMqConsumerService(
        RabbitMqConfiguration configuration,
        IMessageBroadcaster messageBroadcaster)
    {
        this.configuration = configuration;
        this.messageBroadcaster = messageBroadcaster;
    }

    /// <summary>
    /// Starts the RabbitMQ consumer asynchronously, establishing connections, declaring exchanges, binding queues,
    /// and ensuring the consumer runs indefinitely to process incoming messages.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation of starting the RabbitMQ consumer.</returns>
    public async Task StartConsumerAsync()
    {
        Console.WriteLine("Starting RabbitMQ consumer...");

        // Create connection factory with configured hostname and port
        var factory = new ConnectionFactory
        {
            HostName = configuration.HostName,
            Port = configuration.Port
        };

        // Attempt to connect with retry logic
        var connection = await ConnectWithRetryAsync(factory);

        // EO; could not establish connection after max retries
        if (connection == null)
        {
            Console.WriteLine("Could not connect RabbitMQ consumer");
            return;
        }

        // Create channel for communication with RabbitMQ
        var channel = await connection.CreateChannelAsync();

        // Declare exchanges and bind queues
        await DeclareExchangesAsync(channel);
        await SetupQueuesAndConsumerAsync(channel);

        Console.WriteLine("RabbitMQ consumer started");

        // Keep the consumer running indefinitely
        await Task.Delay(Timeout.Infinite);
    }

    /// <summary>
    /// Attempts to connect to RabbitMQ with exponential retry logic
    /// </summary>
    /// <param name="factory">The connection factory configured with hostname and port</param>
    /// <returns>The established connection, or null if all retry attempts fail</returns>
    private async Task<IConnection?> ConnectWithRetryAsync(ConnectionFactory factory)
    {
        int retryCount = 0;

        while (retryCount < configuration.MaxRetries)
        {
            try
            {
                var connection = await factory.CreateConnectionAsync();
                Console.WriteLine("RabbitMQ consumer connected");
                return connection;
            }
            catch (Exception exception)
            {
                retryCount++;
                Console.WriteLine($"RabbitMQ retry {retryCount}/{configuration.MaxRetries}... Error: {exception.Message}");
                await Task.Delay(configuration.RetryDelayMilliseconds);
            }
        }

        return null;
    }

    /// <summary>
    /// Declares all required RabbitMQ exchanges for the relay service
    /// </summary>
    /// <param name="channel">The RabbitMQ channel to declare exchanges on</param>
    /// <remarks>
    /// Declares three exchanges:
    /// - Chat global (fanout) for broadcasting to all users
    /// - Game events (topic) for game-specific routing
    /// - Session events (topic) for session-specific routing
    /// </remarks>
    private async Task DeclareExchangesAsync(IChannel channel)
    {
        await channel.ExchangeDeclareAsync(configuration.ChatGlobalExchange, ExchangeType.Fanout, durable: true);
        await channel.ExchangeDeclareAsync(configuration.GameEventsExchange, ExchangeType.Topic, durable: true);
        await channel.ExchangeDeclareAsync(configuration.SessionEventsExchange, ExchangeType.Topic, durable: true);
    }

    /// <summary>
    /// Creates temporary queues, binds them to exchanges, and sets up the message consumer
    /// </summary>
    /// <param name="channel">The RabbitMQ channel to setup queues and consumer on</param>
    /// <remarks>
    /// Creates three temporary queues (one per exchange) and binds them with appropriate routing keys
    /// Sets up an async consumer that forwards all received messages to WebSocket clients
    /// </remarks>
    private async Task SetupQueuesAndConsumerAsync(IChannel channel)
    {
        // Subscribe to global messages (fanout exchange - empty routing key)
        var globalQueueResult = await channel.QueueDeclareAsync();
        var globalQueue = globalQueueResult.QueueName;
        await channel.QueueBindAsync(globalQueue, configuration.ChatGlobalExchange, "");

        // Subscribe to game events (topic exchange - wildcard routing)
        var gameQueueResult = await channel.QueueDeclareAsync();
        var gameQueue = gameQueueResult.QueueName;
        await channel.QueueBindAsync(gameQueue, configuration.GameEventsExchange, configuration.TopicWildcard);

        // Subscribe to session events (topic exchange - wildcard routing)
        var sessionQueueResult = await channel.QueueDeclareAsync();
        var sessionQueue = sessionQueueResult.QueueName;
        await channel.QueueBindAsync(sessionQueue, configuration.SessionEventsExchange, configuration.TopicWildcard);

        // Setup async consumer with message handler
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += HandleMessageReceivedAsync;

        // Start consuming from all queues with auto-acknowledgment
        await channel.BasicConsumeAsync(globalQueue, autoAck: true, consumer);
        await channel.BasicConsumeAsync(gameQueue, autoAck: true, consumer);
        await channel.BasicConsumeAsync(sessionQueue, autoAck: true, consumer);
    }

    /// <summary>
    /// Handles incoming messages from RabbitMQ and forwards them to all WebSocket clients
    /// </summary>
    /// <param name="model">The consumer model (unused)</param>
    /// <param name="eventArgs">Event arguments containing the message body</param>
    private async Task HandleMessageReceivedAsync(object model, BasicDeliverEventArgs eventArgs)
    {
        var body = eventArgs.Body.ToArray();
        await messageBroadcaster.BroadcastToAllClientsAsync(body);
    }
}
