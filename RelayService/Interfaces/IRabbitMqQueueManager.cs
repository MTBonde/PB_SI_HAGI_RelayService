using RelayService.Services;

namespace RelayService.Interfaces;

/// <summary>
/// Service for managing RabbitMQ queue lifecycle (creation and deletion of user and server queues)
/// </summary>
public interface IRabbitMqQueueManager
{
    event EventHandler<PrivateQueueCreatedEventArgs> PrivateQueueCreated;
    event EventHandler<ServerQueueCreatedEventArgs> ServerQueueCreated;
    
    /// <summary>
    /// Creates and binds a private queue for a specific user to receive direct messages
    /// </summary>
    /// <param name="username">The username for which to create the private queue</param>
    /// <returns>A task representing the asynchronous queue setup operation</returns>
    /// <remarks>
    /// Creates a queue with name "relay.user.{username}" and binds it to the chat.private exchange
    /// with routing key "user.{username}". Queue is exclusive and will auto-delete when user disconnects.
    /// </remarks>
    Task AddPrivateQueueForUserAsync(string username);

    /// <summary>
    /// Removes the private queue for a specific user when they disconnect
    /// </summary>
    /// <param name="username">The username whose private queue should be removed</param>
    /// <returns>A task representing the asynchronous queue removal operation</returns>
    /// <remarks>
    /// Cleans up the per-user queue and consumer when user disconnects.
    /// Queue will auto-delete due to exclusive flag, but this ensures immediate cleanup.
    /// </remarks>
    Task RemovePrivateQueueForUserAsync(string username);

    /// <summary>
    /// Creates and binds a server queue to receive messages for all users on a specific server
    /// </summary>
    /// <param name="serverId">The server ID for which to create the queue</param>
    /// <returns>A task representing the asynchronous queue setup operation</returns>
    /// <remarks>
    /// Creates a queue for server-group messaging.
    /// Queue is non-exclusive and auto-deletes when no consumers remain.
    /// </remarks>
    Task AddServerQueueAsync(string serverId);
}
