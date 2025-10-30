using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using RelayService.Interfaces;
using RelayService.Models;

namespace RelayService.Services;

/// <summary>
/// Manages WebSocket connections lifecycle including connection storage, welcome messages, and message broadcasting
/// </summary>
public class WebSocketConnectionManager : IWebSocketConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocket> connections;
    private readonly WebSocketConfiguration configuration;

    /// <summary>
    /// Initializes a new instance of the WebSocketConnectionManager class with the specified configuration
    /// </summary>
    /// <param name="configuration">WebSocket configuration containing buffer size and welcome message</param>
    public WebSocketConnectionManager(WebSocketConfiguration configuration)
    {
        this.configuration = configuration;
        connections = new ConcurrentDictionary<string, WebSocket>();
    }

    /// <summary>
    /// Adds a new WebSocket connection to the connection manager, associating it with a specific user ID.
    /// </summary>
    /// <param name="userId">The unique identifier for the user associated with the WebSocket connection.</param>
    /// <param name="webSocket">The WebSocket instance representing the user's connection.</param>
    public void AddConnection(string userId, WebSocket webSocket)
    {
        // Store connection in thread-safe dictionary (overwrites existing if user reconnects)
        connections[userId] = webSocket;
        Console.WriteLine($"User {userId} connected. Total connections: {connections.Count}");
    }

    /// <summary>
    /// Removes the WebSocket connection associated with the specified user ID from the connection manager
    /// </summary>
    /// <param name="userId">The unique identifier of the user whose connection is to be removed</param>
    public void RemoveConnection(string userId)
    {
        // Remove connection from dictionary in a thread-safe manner
        connections.TryRemove(userId, out _);
        Console.WriteLine($"User {userId} disconnected. Total connections: {connections.Count}");
    }

    /// <summary>
    /// Sends a welcome message to the connected WebSocket client asynchronously.
    /// </summary>
    /// <param name="webSocket">The WebSocket instance representing the connection to the client.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    public async Task SendWelcomeMessageAsync(WebSocket webSocket)
    {
        // Convert welcome message to UTF-8 bytes and send to client
        var welcomeBytes = Encoding.UTF8.GetBytes(configuration.WelcomeMessage);
        await webSocket.SendAsync(
            new ArraySegment<byte>(welcomeBytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }

    /// <summary>
    /// Broadcasts a message to all connected WebSocket clients.
    /// </summary>
    /// <param name="message">The message to be broadcasted, represented as a byte array.</param>
    /// <returns>A task that represents the asynchronous broadcast operation.</returns>
    public async Task BroadcastMessageAsync(byte[] message)
    {
        // Iterate through all connections and send message to those that are open
        foreach (var connection in connections.Values)
        {
            if (connection.State == WebSocketState.Open)
            {
                try
                {
                    await connection.SendAsync(
                        new ArraySegment<byte>(message),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                }
                catch (Exception exception)
                {
                    // Log error but continue broadcasting to other clients
                    Console.WriteLine($"Error sending to WebSocket: {exception.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Gets the total number of active WebSocket connections currently managed.
    /// </summary>
    /// <returns>The count of active WebSocket connections.</returns>
    public int GetConnectionCount()
    {
        return connections.Count;
    }
}
