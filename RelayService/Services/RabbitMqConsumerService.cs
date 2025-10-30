using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RelayService.Configuration;
using RelayService.Interfaces;
using RelayService.Models;

namespace RelayService.Services;

/// <summary>
/// Service for consuming messages from RabbitMQ exchanges and forwarding them to connected WebSocket clients.
/// Uses shared connection/channel from RabbitMqConnectionManager and supports per-user private queues and per-server queues.
/// </summary>
public class RabbitMqConsumerService : IRabbitMqConsumerService
{
    private readonly RabbitMqConnectionManager connectionManager;
    private readonly RabbitMqConfiguration configuration;
    private readonly IMessageBroadcaster messageBroadcaster;
    private readonly ConcurrentDictionary<string, string> userConsumerTags;
    private readonly ConcurrentDictionary<string, string> serverConsumerTags;

    /// <summary>
    /// Initializes a new instance of the RabbitMqConsumerService class with the specified dependencies
    /// </summary>
    /// <param name="connectionManager">Centralized connection manager providing shared channel</param>
    /// <param name="configuration">RabbitMQ configuration containing connection and exchange settings</param>
    /// <param name="messageBroadcaster">Message broadcaster for forwarding messages to WebSocket clients</param>
    /// <param name="queueManager">Queue manager to subscribe to queue creation events</param>
    public RabbitMqConsumerService(
        RabbitMqConnectionManager connectionManager,
        RabbitMqConfiguration configuration,
        IMessageBroadcaster messageBroadcaster,
        IRabbitMqQueueManager queueManager)
    {
        this.connectionManager = connectionManager;
        this.configuration = configuration;
        this.messageBroadcaster = messageBroadcaster;
        this.userConsumerTags = new ConcurrentDictionary<string, string>();
        this.serverConsumerTags = new ConcurrentDictionary<string, string>();

        // Subscribe to queue creation events
        queueManager.PrivateQueueCreated += OnPrivateQueueCreated;
        queueManager.ServerQueueCreated += OnServerQueueCreated;
    }

    /// <summary>
    /// Starts the RabbitMQ consumer asynchronously using shared connection/channel
    /// </summary>
    /// <returns>A task that represents the asynchronous operation of starting the RabbitMQ consumer.</returns>
    public async Task StartConsumerAsync()
    {
        Console.WriteLine("Starting RabbitMQ consumer...");

        // Get shared channel from connection manager
        var channel = await connectionManager.GetChannelAsync();

        // Declare exchanges and bind queues
        await DeclareExchangesAsync(channel);
        await SetupQueuesAndConsumerAsync(channel);

        Console.WriteLine("RabbitMQ consumer started using shared connection/channel");

        // Keep the consumer running indefinitely
        await Task.Delay(Timeout.Infinite);
    }

    /// <summary>
    /// Declares all required RabbitMQ exchanges for the relay service:
    /// chat.private (direct) for 1:1 messages, chat.server (topic) for server-group messages, and chat.global (fanout) for broadcasts.
    /// </summary>
    /// <param name="channel">The RabbitMQ channel to declare exchanges on</param>
    private async Task DeclareExchangesAsync(IChannel channel)
    {
        // Declare chat.global exchange for broadcast messages
        await channel.ExchangeDeclareAsync(
            exchange: configuration.ChatGlobalExchange,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false);
        Console.WriteLine($"Consumer declared exchange: {configuration.ChatGlobalExchange} (fanout)");

        // Declare chat.private exchange for private messages
        await channel.ExchangeDeclareAsync(
            exchange: configuration.ChatPrivateExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);
        Console.WriteLine($"Consumer declared exchange: {configuration.ChatPrivateExchange} (direct)");

        // Declare chat.server exchange for server-group messages
        await channel.ExchangeDeclareAsync(
            exchange: configuration.ChatServerExchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);
        Console.WriteLine($"Consumer declared exchange: {configuration.ChatServerExchange} (topic)");
    }

    /// <summary>
    /// Creates queues, binds them to exchanges, and sets up the message consumer
    /// </summary>
    /// <param name="channel">The RabbitMQ channel to setup queues and consumer on</param>
    private async Task SetupQueuesAndConsumerAsync(IChannel channel)
    {
        // Subscribe to global broadcast messages
        var globalQueueName = "relay.global";
        await channel.QueueDeclareAsync(
            queue: globalQueueName,
            durable: false,
            exclusive: false,
            autoDelete: true);
        await channel.QueueBindAsync(
            queue: globalQueueName,
            exchange: configuration.ChatGlobalExchange,
            routingKey: "");
        Console.WriteLine($"Declared and bound global queue: {globalQueueName} to {configuration.ChatGlobalExchange}");

        // Setup async consumer with message handler
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += HandleGlobalMessageReceivedAsync;

        // Start consuming from global queue with auto-acknowledgment
        await channel.BasicConsumeAsync(globalQueueName, autoAck: true, consumer);
        Console.WriteLine($"Started consuming from {globalQueueName}");
    }

    /// <summary>
    /// Handles incoming global chat messages from RabbitMQ and forwards them to all WebSocket clients
    /// </summary>
    /// <param name="model">The consumer model (unused)</param>
    /// <param name="eventArgs">Event arguments containing the message body</param>
    private async Task HandleGlobalMessageReceivedAsync(object model, BasicDeliverEventArgs eventArgs)
    {
        try
        {
            // Deserialize message from JSON
            var body = eventArgs.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var chatMessage = JsonSerializer.Deserialize<ChatMessage>(json);

            if (chatMessage == null)
            {
                Console.WriteLine("Failed to deserialize chat message");
                return;
            }

            Console.WriteLine($"Received global message from {chatMessage.FromUsername}: {chatMessage.Content}");

            // Broadcast to all connected WebSocket clients
            await messageBroadcaster.BroadcastChatMessageAsync(chatMessage);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error handling global message: {exception.Message}");
        }
    }

    /// <summary>
    /// Event handler for when a private queue is created by the queue manager.
    /// Sets up the consumer for the newly created queue.
    /// </summary>
    private async void OnPrivateQueueCreated(object? sender, PrivateQueueCreatedEventArgs e)
    {
        try
        {
            var channel = await connectionManager.GetChannelAsync();

            // Setup consumer for this private queue
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, eventArgs) =>
            {
                await HandlePrivateMessageReceivedAsync(e.Username, model, eventArgs);
            };

            // Start consuming from private queue
            var consumerTag = await channel.BasicConsumeAsync(e.QueueName, autoAck: true, consumer);

            // Store consumer tag for later cleanup
            userConsumerTags[e.Username] = consumerTag;

            Console.WriteLine($"Started consuming from private queue: {e.QueueName} (consumer tag: {consumerTag})");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error setting up consumer for private queue {e.QueueName}: {exception.Message}");
        }
    }


    /// <summary>
    /// Handles incoming private chat messages from RabbitMQ and forwards them to the specific recipient
    /// </summary>
    /// <param name="recipientUsername">The username of the recipient</param>
    /// <param name="model">The consumer model (unused)</param>
    /// <param name="eventArgs">Event arguments containing the message body</param>
    private async Task HandlePrivateMessageReceivedAsync(string recipientUsername, object model, BasicDeliverEventArgs eventArgs)
    {
        try
        {
            // Deserialize message from JSON
            var body = eventArgs.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var chatMessage = JsonSerializer.Deserialize<ChatMessage>(json);

            if (chatMessage == null)
            {
                Console.WriteLine("Failed to deserialize private chat message");
                return;
            }

            Console.WriteLine($"Received private message from {chatMessage.FromUsername} to {recipientUsername}: {chatMessage.Content}");

            // Send to specific recipient only
            await messageBroadcaster.SendChatMessageToUserAsync(recipientUsername, chatMessage);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error handling private message for user {recipientUsername}: {exception.Message}");
        }
    }

    /// <summary>
    /// Event handler for when a server queue is created by the queue manager.
    /// Sets up the consumer for the newly created queue.
    /// </summary>
    private async void OnServerQueueCreated(object? sender, ServerQueueCreatedEventArgs e)
    {
        // Check if consumer already exists
        if (serverConsumerTags.ContainsKey(e.ServerId))
        {
            Console.WriteLine($"Consumer for server queue {e.ServerId} already exists - skipping");
            return;
        }

        try
        {
            var channel = await connectionManager.GetChannelAsync();

            // Setup consumer for this server queue
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, eventArgs) =>
            {
                await HandleServerMessageReceivedAsync(e.ServerId, model, eventArgs);
            };

            // Start consuming from server queue
            var consumerTag = await channel.BasicConsumeAsync(e.QueueName, autoAck: true, consumer);

            // Store consumer tag for later cleanup
            serverConsumerTags[e.ServerId] = consumerTag;

            Console.WriteLine($"Started consuming from server queue: {e.QueueName} (consumer tag: {consumerTag})");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error setting up consumer for server queue {e.QueueName}: {exception.Message}");
        }
    }

    /// <summary>
    /// Handles incoming server chat messages from RabbitMQ and forwards them to all users on the server
    /// </summary>
    /// <param name="serverId">The server ID for this message</param>
    /// <param name="model">The consumer model (unused)</param>
    /// <param name="eventArgs">Event arguments containing the message body</param>
    private async Task HandleServerMessageReceivedAsync(string serverId, object model, BasicDeliverEventArgs eventArgs)
    {
        try
        {
            // Deserialize message from JSON
            var body = eventArgs.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            var chatMessage = JsonSerializer.Deserialize<ChatMessage>(json);

            if (chatMessage == null)
            {
                Console.WriteLine("Failed to deserialize server chat message");
                return;
            }

            Console.WriteLine($"Received server message from {chatMessage.FromUsername} for server {serverId}: {chatMessage.Content}");

            // Broadcast to all users on this server
            await messageBroadcaster.BroadcastChatMessageToServerAsync(serverId, chatMessage);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error handling server message for server {serverId}: {exception.Message}");
        }
    }
}
