using RelayService.Models;

namespace RelayService.Interfaces;

/// <summary>
/// Service for broadcasting messages from RabbitMQ to connected WebSocket clients
/// </summary>
public interface IMessageBroadcaster
{
    /// <summary>
    /// Broadcasts a message to all connected WebSocket clients
    /// </summary>
    /// <param name="message">The message bytes to broadcast to all clients</param>
    /// <returns>A task representing the asynchronous broadcast operation</returns>
    Task BroadcastToAllClientsAsync(byte[] message);

    /// <summary>
    /// Broadcasts a chat message to all connected WebSocket clients with JSON serialization
    /// </summary>
    /// <param name="chatMessage">The chat message object to serialize and broadcast</param>
    /// <returns>A task representing the asynchronous broadcast operation</returns>
    Task BroadcastChatMessageAsync(ChatMessage chatMessage);

    /// <summary>
    /// Sends a chat message to a specific user with JSON serialization
    /// </summary>
    /// <param name="username">The username of the recipient</param>
    /// <param name="chatMessage">The chat message object to serialize and send</param>
    /// <returns>A task representing the asynchronous send operation</returns>
    /// <remarks>Sends message only to the specified user if they are connected</remarks>
    Task SendChatMessageToUserAsync(string username, ChatMessage chatMessage);

    /// <summary>
    /// Broadcasts a chat message to all users on a specific server with JSON serialization
    /// </summary>
    /// <param name="serverId">The server ID to broadcast to</param>
    /// <param name="chatMessage">The chat message object to serialize and broadcast</param>
    /// <returns>A task representing the asynchronous broadcast operation</returns>
    /// <remarks>Sends message only to users connected to the specified server</remarks>
    Task BroadcastChatMessageToServerAsync(string serverId, ChatMessage chatMessage);
}
