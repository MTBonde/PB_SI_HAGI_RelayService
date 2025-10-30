using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using RelayService.Interfaces;
using RelayService.Models;
using System.Text.Json;

namespace RelayService.Services;

/// <summary>
/// Manages WebSocket connections lifecycle including connection storage, welcome messages, and message broadcasting
/// </summary>
public class WebSocketConnectionManager : IWebSocketConnectionManager
{
    private readonly ConcurrentDictionary<string, UserConnection> connections;
    private readonly WebSocketConfiguration configuration;
    private readonly IRabbitMqPublisher rabbitMqPublisher;
    private readonly IRabbitMqQueueManager rabbitMqQueueManager;
    private readonly ISessionServiceClient sessionServiceClient;

    /// <summary>
    /// Initializes a new instance of the WebSocketConnectionManager class with the specified configuration
    /// </summary>
    /// <param name="configuration">WebSocket configuration containing buffer size and welcome message</param>
    /// <param name="rabbitMqPublisher">RabbitMQ publisher for presence events</param>
    /// <param name="rabbitMqQueueManager">RabbitMQ queue manager for managing per-user and per-server queues</param>
    /// <param name="sessionServiceClient">SessionService client for tracking user server assignments</param>
    public WebSocketConnectionManager(
        WebSocketConfiguration configuration,
        IRabbitMqPublisher rabbitMqPublisher,
        IRabbitMqQueueManager rabbitMqQueueManager,
        ISessionServiceClient sessionServiceClient)
    {
        this.configuration = configuration;
        this.rabbitMqPublisher = rabbitMqPublisher;
        this.rabbitMqQueueManager = rabbitMqQueueManager;
        this.sessionServiceClient = sessionServiceClient;
        connections = new ConcurrentDictionary<string, UserConnection>();
    }

    /// <summary>
    /// Adds a new WebSocket connection to the connection manager, associating it with a specific user ID.
    /// Creates a private RabbitMQ queue for direct messages and retrieves the user's server ID from SessionService.
    /// </summary>
    /// <param name="username">The unique identifier for the user associated with the WebSocket connection.</param>
    /// <param name="role">The role of the user (from JWT claims)</param>
    /// <param name="webSocket">The WebSocket instance representing the user's connection.</param>
    public void AddConnection(string username, string role, WebSocket webSocket)
    {
        // Generate unique connection ID
        var connectionId = Guid.NewGuid().ToString();

        // Get server ID from SessionService
        var serverId = sessionServiceClient.GetServerIdAsync(username).GetAwaiter().GetResult();

        // Store connection with server tracking (overwrites existing if user reconnects)
        var userConnection = new UserConnection
        {
            WebSocket = webSocket,
            Username = username,
            Role = role,
            ServerId = serverId,
            ConnectionId = connectionId
        };
        connections[username] = userConnection;
        Console.WriteLine($"User {username} connected to server {serverId ?? "none"}. Total connections: {connections.Count}");

        // Create private queue for this user
        rabbitMqQueueManager.AddPrivateQueueForUserAsync(username).GetAwaiter().GetResult();

        // Create server queue if user is on a server
        if (!string.IsNullOrEmpty(serverId))
        {
            rabbitMqQueueManager.AddServerQueueAsync(serverId).GetAwaiter().GetResult();
        }

        // Notify SessionService of login
        sessionServiceClient.LoginAsync(username, role, connectionId, serverId).GetAwaiter().GetResult();

        // Publish presence event to RabbitMQ
        PublishPresenceEventAsync("connected", username, role, serverId).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Removes the WebSocket connection associated with the specified user ID from the connection manager.
    /// Also removes the user's private RabbitMQ queue and notifies SessionService of logout.
    /// </summary>
    /// <param name="username">The unique identifier of the user whose connection is to be removed</param>
    /// <param name="role">The role of the user (from JWT claims)</param>
    public void RemoveConnection(string username, string role)
    {
        // Get connection info before removing
        string? serverId = null;
        string? connectionId = null;
        if (connections.TryGetValue(username, out var userConnection))
        {
            serverId = userConnection.ServerId;
            connectionId = userConnection.ConnectionId;
        }

        // Remove connection from dictionary in a thread-safe manner
        connections.TryRemove(username, out _);
        Console.WriteLine($"User {username} disconnected. Total connections: {connections.Count}");

        // Remove private queue for this user
        rabbitMqQueueManager.RemovePrivateQueueForUserAsync(username).GetAwaiter().GetResult();

        // Notify SessionService of logout
        if (connectionId != null)
        {
            sessionServiceClient.LogoutAsync(username, connectionId).GetAwaiter().GetResult();
        }

        // Publish presence event to RabbitMQ
        PublishPresenceEventAsync("disconnected", username, role, serverId).GetAwaiter().GetResult();
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
        foreach (var userConnection in connections.Values)
        {
            if (userConnection.WebSocket.State == WebSocketState.Open)
            {
                try
                {
                    var messageText = Encoding.UTF8.GetString(message);
                    Console.WriteLine($"Attempting to send: {messageText}...");
                    await userConnection.WebSocket.SendAsync(
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
    /// Sends a message to a specific user by username
    /// </summary>
    /// <param name="username">The username of the recipient</param>
    /// <param name="message">The message bytes to send</param>
    /// <returns>A task representing the asynchronous send operation</returns>
    /// <remarks>Message is only sent if the user is connected and their WebSocket is in Open state</remarks>
    public async Task SendMessageToUserAsync(string username, byte[] message)
    {
        // Check if user is connected
        if (connections.TryGetValue(username, out var userConnection))
        {
            if (userConnection.WebSocket.State == WebSocketState.Open)
            {
                try
                {
                    var messageText = Encoding.UTF8.GetString(message);
                    Console.WriteLine($"Attempting to send private message to user {username}: {messageText}...");
                    await userConnection.WebSocket.SendAsync(
                        new ArraySegment<byte>(message),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                    Console.WriteLine($"Sent private message to user {username}");
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"Error sending private message to user {username}: {exception.Message}");
                }
            }
            else
            {
                Console.WriteLine($"WebSocket for user {username} is not in Open state (state: {userConnection.WebSocket.State})");
            }
        }
        else
        {
            Console.WriteLine($"User {username} is not connected - message not delivered");
        }
    }

    /// <summary>
    /// Broadcasts a message to all users connected to a specific server
    /// </summary>
    /// <param name="serverId">The server ID to broadcast to</param>
    /// <param name="message">The message bytes to send</param>
    /// <returns>A task representing the asynchronous broadcast operation</returns>
    /// <remarks>Only sends to users whose ServerId matches the specified server</remarks>
    public async Task BroadcastMessageToServerAsync(string serverId, byte[] message)
    {
        var messageText = Encoding.UTF8.GetString(message);
        Console.WriteLine($"Broadcasting to server {serverId}: {messageText}");

        var sentCount = 0;
        foreach (var userConnection in connections.Values)
        {
            // Only send to users on the specified server
            if (userConnection.ServerId == serverId && userConnection.WebSocket.State == WebSocketState.Open)
            {
                try
                {
                    await userConnection.WebSocket.SendAsync(
                        new ArraySegment<byte>(message),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                    sentCount++;
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"Error sending server message to user {userConnection.Username}: {exception.Message}");
                }
            }
        }

        Console.WriteLine($"Sent server message to {sentCount} users on server {serverId}");
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
    /// <param name="username">The username associated with the presence event</param>
    /// <param name="role">The role of the user</param>
    /// <param name="serverId">The server ID where the user is connected</param>
    private async Task PublishPresenceEventAsync(string eventType, string username, string role, string? serverId = null)
    {
        try
        {
            // Create presence event with basic information
            var presenceEvent = new
            {
                eventType = eventType,
                username = username,
                role = role,
                serverId = serverId,
                timestamp = DateTime.UtcNow,
                // TODO: Next iteration - add user context from JWT claims
                // userId = userId,
            };

            var message = JsonSerializer.Serialize(presenceEvent);
            await rabbitMqPublisher.PublishAsync("relay.session.events", $"user.{eventType}", message);

            Console.WriteLine($"Published presence event: {eventType} for user {username} (server: {serverId ?? "none"})");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error publishing presence event: {exception.Message}");
        }
    }
}

/// <summary>
/// Represents a user connection with associated metadata
/// </summary>
internal class UserConnection
{
    /// <summary>
    /// The WebSocket connection for this user
    /// </summary>
    public required WebSocket WebSocket { get; init; }

    /// <summary>
    /// The username of the connected user
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// The role of the connected user (from JWT claims)
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// The unique connection identifier for this WebSocket session
    /// </summary>
    public required string ConnectionId { get; init; }

    /// <summary>
    /// The server ID where the user is currently connected
    /// </summary>
    /// <remarks>
    /// Null if the user is not assigned to a specific server.
    /// Used for routing server-group messages to users on the same game server.
    /// </remarks>
    public string? ServerId { get; init; }
}
