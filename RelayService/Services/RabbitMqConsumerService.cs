using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RelayService.Configuration;
using RelayService.Interfaces;
using RelayService.Models;

namespace RelayService.Services;

/// <summary>
/// Service for consuming messages from RabbitMQ exchanges and forwarding them to connected WebSocket clients
/// Uses shared connection/channel from RabbitMqConnectionManager
/// </summary>
/// <remarks>
/// Phase 1.b refactor: Uses centralized connection manager for single connection/channel
/// </remarks>
public class RabbitMqConsumerService : IRabbitMqConsumerService
{
    private readonly RabbitMqConnectionManager connectionManager;
    private readonly RabbitMqConfiguration configuration;
    private readonly IMessageBroadcaster messageBroadcaster;

    /// <summary>
    /// Initializes a new instance of the RabbitMqConsumerService class with the specified dependencies
    /// </summary>
    /// <param name="connectionManager">Centralized connection manager providing shared channel</param>
    /// <param name="configuration">RabbitMQ configuration containing connection and exchange settings</param>
    /// <param name="messageBroadcaster">Message broadcaster for forwarding messages to WebSocket clients</param>
    public RabbitMqConsumerService(
        RabbitMqConnectionManager connectionManager,
        RabbitMqConfiguration configuration,
        IMessageBroadcaster messageBroadcaster)
    {
        this.connectionManager = connectionManager;
        this.configuration = configuration;
        this.messageBroadcaster = messageBroadcaster;
    }

    /// <summary>
    /// Starts the RabbitMQ consumer asynchronously using shared connection/channel
    /// </summary>
    /// <returns>A task that represents the asynchronous operation of starting the RabbitMQ consumer.</returns>
    /// <remarks>
    /// Uses shared channel from RabbitMqConnectionManager - no connection creation here
    /// </remarks>
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
    /// Declares all required RabbitMQ exchanges for the relay service
    /// </summary>
    /// <param name="channel">The RabbitMQ channel to declare exchanges on</param>
    /// <remarks>
    /// Declares three exchanges:
    /// - chat.private (direct): For private 1:1 messages using routing key user.{username}
    /// - chat.server (topic): For server-group messages using routing key server.{serverId}
    /// - chat.global (fanout): For global broadcast messages
    /// </remarks>
    private async Task DeclareExchangesAsync(IChannel channel)
    {
        // Declare chat.global exchange for broadcast messages
        await channel.ExchangeDeclareAsync(
            exchange: configuration.ChatGlobalExchange,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false);
        Console.WriteLine($"Consumer declared exchange: {configuration.ChatGlobalExchange} (fanout)");

        // Declare chat.private exchange for private messages (Phase 2)
        await channel.ExchangeDeclareAsync(
            exchange: configuration.ChatPrivateExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);
        Console.WriteLine($"Consumer declared exchange: {configuration.ChatPrivateExchange} (direct)");

        // Declare chat.server exchange for server-group messages (Phase 3)
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
        // PHASE 1: Subscribe to global broadcast messages
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
}
