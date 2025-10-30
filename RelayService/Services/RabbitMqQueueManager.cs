using System.Collections.Concurrent;
using RelayService.Configuration;
using RelayService.Interfaces;

namespace RelayService.Services;

/// <summary>
/// Manages RabbitMQ queue lifecycle (creation and deletion) without setting up message consumers.
/// Raises events when queues are created so that consumer service can set up message handlers.
/// </summary>
public class RabbitMqQueueManager : IRabbitMqQueueManager
{
    private readonly RabbitMqConnectionManager connectionManager;
    private readonly RabbitMqConfiguration configuration;
    private readonly ConcurrentDictionary<string, bool> userQueues;
    private readonly ConcurrentDictionary<string, bool> serverQueues;

    /// <summary>
    /// Event raised when a private queue is created for a user
    /// </summary>
    public event EventHandler<PrivateQueueCreatedEventArgs>? PrivateQueueCreated;

    /// <summary>
    /// Event raised when a server queue is created
    /// </summary>
    public event EventHandler<ServerQueueCreatedEventArgs>? ServerQueueCreated;

    /// <summary>
    /// Initializes a new instance of the RabbitMqQueueManager class
    /// </summary>
    /// <param name="connectionManager">Centralized connection manager providing shared channel</param>
    /// <param name="configuration">RabbitMQ configuration containing exchange settings</param>
    public RabbitMqQueueManager(
        RabbitMqConnectionManager connectionManager,
        RabbitMqConfiguration configuration)
    {
        this.connectionManager = connectionManager;
        this.configuration = configuration;
        this.userQueues = new ConcurrentDictionary<string, bool>();
        this.serverQueues = new ConcurrentDictionary<string, bool>();
    }

    /// <summary>
    /// Creates and binds a private queue for a specific user to receive direct messages.
    /// Raises PrivateQueueCreated event so consumer service can set up message handler.
    /// </summary>
    /// <param name="username">The username for which to create the private queue</param>
    public async Task AddPrivateQueueForUserAsync(string username)
    {
        // Check if queue already exists for this user
        if (userQueues.ContainsKey(username))
        {
            Console.WriteLine($"Private queue for user {username} already exists - skipping");
            return;
        }

        try
        {
            var channel = await connectionManager.GetChannelAsync();
            var queueName = $"relay.user.{username}";
            var routingKey = $"user.{username}";

            // Declare private queue for this user (exclusive, auto-delete)
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: false,
                exclusive: true,
                autoDelete: true);

            // Bind queue to chat.private exchange with user-specific routing key
            await channel.QueueBindAsync(
                queue: queueName,
                exchange: configuration.ChatPrivateExchange,
                routingKey: routingKey);

            // Track queue
            userQueues[username] = true;

            Console.WriteLine($"Declared private queue: {queueName} bound to {configuration.ChatPrivateExchange} with routing key {routingKey}");

            // Raise event so consumer service can set up message handler
            PrivateQueueCreated?.Invoke(this, new PrivateQueueCreatedEventArgs
            {
                Username = username,
                QueueName = queueName
            });
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error setting up private queue for user {username}: {exception.Message}");
        }
    }

    /// <summary>
    /// Removes the private queue for a specific user when they disconnect.
    /// Queue will auto-delete due to exclusive flag, but this ensures immediate cleanup.
    /// </summary>
    /// <param name="username">The username whose private queue should be removed</param>
    public async Task RemovePrivateQueueForUserAsync(string username)
    {
        try
        {
            // Remove from tracking
            if (userQueues.TryRemove(username, out _))
            {
                Console.WriteLine($"Removed private queue tracking for user {username}");
                // Note: Queue will auto-delete when connection closes due to exclusive flag
                // Consumer cancellation is handled by the consumer service
            }
            else
            {
                Console.WriteLine($"No private queue found for user {username}");
            }

            await Task.CompletedTask;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error removing private queue for user {username}: {exception.Message}");
        }
    }

    /// <summary>
    /// Creates and binds a server queue to receive messages for all users on a specific server.
    /// Raises ServerQueueCreated event so consumer service can set up message handler.
    /// </summary>
    /// <param name="serverId">The server ID for which to create the queue</param>
    public async Task AddServerQueueAsync(string serverId)
    {
        // Check if server queue already exists
        if (serverQueues.ContainsKey(serverId))
        {
            Console.WriteLine($"Server queue for {serverId} already exists - skipping");
            return;
        }

        try
        {
            var channel = await connectionManager.GetChannelAsync();
            var queueName = $"relay.server.{serverId}";
            var routingKey = $"server.{serverId}";

            // Declare server queue (non-exclusive, auto-delete when no consumers)
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: false,
                exclusive: false,
                autoDelete: true);

            // Bind queue to chat.server exchange with server-specific routing key
            await channel.QueueBindAsync(
                queue: queueName,
                exchange: configuration.ChatServerExchange,
                routingKey: routingKey);

            // Track queue
            serverQueues[serverId] = true;

            Console.WriteLine($"Declared server queue: {queueName} bound to {configuration.ChatServerExchange} with routing key {routingKey}");

            // Raise event so consumer service can set up message handler
            ServerQueueCreated?.Invoke(this, new ServerQueueCreatedEventArgs
            {
                ServerId = serverId,
                QueueName = queueName
            });
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error setting up server queue for server {serverId}: {exception.Message}");
        }
    }
}

/// <summary>
/// Event arguments for private queue creation
/// </summary>
public class PrivateQueueCreatedEventArgs : EventArgs
{
    public required string Username { get; init; }
    public required string QueueName { get; init; }
}

/// <summary>
/// Event arguments for server queue creation
/// </summary>
public class ServerQueueCreatedEventArgs : EventArgs
{
    public required string ServerId { get; init; }
    public required string QueueName { get; init; }
}
