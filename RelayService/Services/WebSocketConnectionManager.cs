using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RelayService.Configuration;
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
    private readonly IRabbitMqPublisher rabbitMqPublisher;

    /// <summary>
    /// Initializes a new instance of the WebSocketConnectionManager class with the specified configuration
    /// </summary>
    /// <param name="configuration">WebSocket configuration containing buffer size and welcome message</param>
    /// <param name="rabbitMqPublisher">RabbitMQ publisher for sending presence events</param>
    public WebSocketConnectionManager(
        WebSocketConfiguration configuration,
        IRabbitMqPublisher rabbitMqPublisher)
    {
        this.configuration = configuration;
        this.rabbitMqPublisher = rabbitMqPublisher;
        connections = new ConcurrentDictionary<string, WebSocket>();
    }

    /// <summary>
    /// Adds a new WebSocket connection to the connection manager, associating it with a specific user ID.
    /// </summary>
    /// <param name="userId">The unique identifier for the user associated with the WebSocket connection.</param>
    /// <param name="webSocket">The WebSocket instance representing the user's connection.</param>
    public void AddConnection(string username, string role, WebSocket webSocket)
    {
        // Store connection in thread-safe dictionary (overwrites existing if user reconnects)
        connections[username] = webSocket;
        Console.WriteLine($"User {username} connected. Total connections: {connections.Count}");

        // Publish presence event to RabbitMQ
        PublishPresenceEventAsync("connected", username, role).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Removes the WebSocket connection associated with the specified user ID from the connection manager
    /// </summary>
    /// <param name="userId">The unique identifier of the user whose connection is to be removed</param>
    public void RemoveConnection(string username, string role)
    {
        // Remove connection from dictionary in a thread-safe manner
        connections.TryRemove(username, out _);
        Console.WriteLine($"User {username} disconnected. Total connections: {connections.Count}");

        // Publish presence event to RabbitMQ
        PublishPresenceEventAsync("disconnected", username, role).GetAwaiter().GetResult();
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
                    var messageText = Encoding.UTF8.GetString(message);
                    Console.WriteLine($"Attempting to send: {messageText}...");
                    await connection.SendAsync(
                        new ArraySegment<byte>(message),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                    Console.WriteLine($"Sent message to client:{messageText}");
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

    /// <summary>
    /// Publishes a presence event to RabbitMQ when a user connects or disconnects
    /// </summary>
    /// <param name="eventType">The type of event: "connected" or "disconnected"</param>
    /// <param name="userId">The user ID associated with the presence event</param>
    private async Task PublishPresenceEventAsync(string eventType, string username, string role)
    {
        try
        {
            // Create presence event with basic information
            var presenceEvent = new
            {
                eventType = eventType,
                username = username,
                role = role,
                timestamp = DateTime.UtcNow,
                // TODO: Next iteration - add user context from JWT claims
                // userId = userId,
            };

            var message = JsonSerializer.Serialize(presenceEvent);
            await rabbitMqPublisher.PublishAsync("relay.session.events", $"user.{eventType}", message);

            Console.WriteLine($"Published presence event: {eventType} for user {username}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error publishing presence event: {exception.Message}");
        }
    }
}
