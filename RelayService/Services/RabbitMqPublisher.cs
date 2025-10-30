using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RelayService.Configuration;
using RelayService.Interfaces;
using RelayService.Models;

namespace RelayService.Services;

/// <summary>
/// Service for publishing messages to RabbitMQ exchanges
/// Uses shared connection/channel from RabbitMqConnectionManager
/// </summary>
/// <remarks>
/// Phase 1.b refactor: Uses centralized connection manager for single connection/channel
/// </remarks>
public class RabbitMqPublisher : IRabbitMqPublisher
{
    private readonly RabbitMqConnectionManager connectionManager;
    private readonly RabbitMqConfiguration configuration;
    private bool exchangesDeclared = false;
    private readonly SemaphoreSlim declareLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the RabbitMqPublisher class with the specified dependencies
    /// </summary>
    /// <param name="connectionManager">Centralized connection manager providing shared channel</param>
    /// <param name="configuration">RabbitMQ configuration containing exchange settings</param>
    public RabbitMqPublisher(
        RabbitMqConnectionManager connectionManager,
        RabbitMqConfiguration configuration)
    {
        this.connectionManager = connectionManager;
        this.configuration = configuration;
    }

    /// <summary>
    /// Publishes a message to the specified RabbitMQ exchange
    /// </summary>
    /// <param name="exchange">The name of the exchange to publish to</param>
    /// <param name="routingKey">The routing key for the message (use empty string for fanout exchanges)</param>
    /// <param name="message">The message content as a string</param>
    public async Task PublishAsync(string exchange, string routingKey, string message)
    {
        try
        {
            // Ensure exchanges are declared (once)
            await EnsureExchangesDeclaredAsync();

            // Convert message to byte array
            var body = Encoding.UTF8.GetBytes(message);

            // Publish using shared connection manager (thread-safe)
            await connectionManager.PublishAsync(exchange, routingKey, body);

            Console.WriteLine($"Published message to exchange '{exchange}' with routing key '{routingKey}'");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error publishing message to RabbitMQ: {exception.Message}");
        }
    }

    /// <summary>
    /// Publishes a chat message to the specified RabbitMQ exchange with automatic JSON serialization
    /// </summary>
    /// <param name="exchange">The name of the exchange to publish to</param>
    /// <param name="routingKey">The routing key for the message (use empty string for fanout exchanges)</param>
    /// <param name="chatMessage">The chat message object to serialize and publish</param>
    public async Task PublishChatMessageAsync(string exchange, string routingKey, ChatMessage chatMessage)
    {
        try
        {
            // Ensure exchanges are declared (once)
            await EnsureExchangesDeclaredAsync();

            // Serialize chat message to JSON
            var json = JsonSerializer.Serialize(chatMessage);
            var body = Encoding.UTF8.GetBytes(json);

            // Publish using shared connection manager (thread-safe)
            await connectionManager.PublishAsync(exchange, routingKey, body);

            Console.WriteLine($"Published chat message from '{chatMessage.FromUsername}' to exchange '{exchange}' with routing key '{routingKey}'");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error publishing chat message to RabbitMQ: {exception.Message}");
        }
    }

    /// <summary>
    /// Ensures exchanges are declared exactly once on first publish
    /// </summary>
    /// <remarks>
    /// Thread-safe with double-check locking pattern
    /// Exchanges are idempotent - safe to declare multiple times, but we optimize by doing it once
    /// </remarks>
    private async Task EnsureExchangesDeclaredAsync()
    {
        if (exchangesDeclared) return;

        await declareLock.WaitAsync();
        try
        {
            if (exchangesDeclared) return;

            var channel = await connectionManager.GetChannelAsync();
            await DeclareExchangesAsync(channel);
            exchangesDeclared = true;
        }
        finally
        {
            declareLock.Release();
        }
    }

    /// <summary>
    /// Declares the chat exchanges required for message routing
    /// </summary>
    /// <param name="channel">The channel to use for declaring exchanges</param>
    /// <remarks>
    /// Declares three exchanges:
    /// - chat.private (direct): For private 1:1 messages using routing key user.{username}
    /// - chat.server (topic): For server-group messages using routing key server.{serverId}
    /// - chat.global (fanout): For global broadcast messages
    /// </remarks>
    private async Task DeclareExchangesAsync(IChannel channel)
    {
        try
        {
            // Declare private message exchange (direct routing)
            await channel.ExchangeDeclareAsync(
                exchange: configuration.ChatPrivateExchange,
                type: "direct",
                durable: true,
                autoDelete: false);
            Console.WriteLine($"Declared exchange: {configuration.ChatPrivateExchange} (direct)");

            // Declare server message exchange (topic routing)
            await channel.ExchangeDeclareAsync(
                exchange: configuration.ChatServerExchange,
                type: "topic",
                durable: true,
                autoDelete: false);
            Console.WriteLine($"Declared exchange: {configuration.ChatServerExchange} (topic)");

            // Declare global message exchange (fanout routing)
            await channel.ExchangeDeclareAsync(
                exchange: configuration.ChatGlobalExchange,
                type: "fanout",
                durable: true,
                autoDelete: false);
            Console.WriteLine($"Declared exchange: {configuration.ChatGlobalExchange} (fanout)");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error declaring exchanges: {exception.Message}");
        }
    }
}
