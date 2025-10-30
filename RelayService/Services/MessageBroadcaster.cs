using System.Text;
using System.Text.Json;
using RelayService.Interfaces;
using RelayService.Models;

namespace RelayService.Services;

/// <summary>
/// Service for broadcasting messages from RabbitMQ to all connected WebSocket clients
/// Acts as a bridge between RabbitMQ consumer and WebSocket connection manager
/// </summary>
public class MessageBroadcaster : IMessageBroadcaster
{
    private readonly IWebSocketConnectionManager connectionManager;

    /// <summary>
    /// Initializes a new instance of the MessageBroadcaster class with the specified connection manager
    /// </summary>
    /// <param name="connectionManager">The WebSocket connection manager to broadcast messages through</param>
    public MessageBroadcaster(IWebSocketConnectionManager connectionManager)
    {
        this.connectionManager = connectionManager;
    }

    /// <summary>
    /// Broadcasts a message to all connected WebSocket clients asynchronously.
    /// </summary>
    /// <param name="message">The byte array containing the message to be broadcasted to all clients.</param>
    /// <returns>A task that represents the asynchronous broadcast operation.</returns>
    public async Task BroadcastToAllClientsAsync(byte[] message)
    {
        // Delegate broadcasting to the connection manager
        await connectionManager.BroadcastMessageAsync(message);
    }

    /// <summary>
    /// Broadcasts a chat message to all connected WebSocket clients with JSON serialization
    /// </summary>
    /// <param name="chatMessage">The chat message object to serialize and broadcast</param>
    /// <returns>A task representing the asynchronous broadcast operation</returns>
    public async Task BroadcastChatMessageAsync(ChatMessage chatMessage)
    {
        // Serialize chat message to JSON
        var json = JsonSerializer.Serialize(chatMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        // Delegate broadcasting to the connection manager
        await connectionManager.BroadcastMessageAsync(bytes);

        Console.WriteLine($"Broadcasted chat message from {chatMessage.FromUsername} to all connected clients");
    }
}
