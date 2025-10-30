using RelayService.Models;

namespace RelayService.Interfaces;

/// <summary>
/// Service for broadcasting messages from RabbitMQ to all connected WebSocket clients
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
}
