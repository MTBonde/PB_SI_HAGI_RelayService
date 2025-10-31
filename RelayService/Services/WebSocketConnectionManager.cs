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

        // DON'T get server ID on connect - user will send server.join message after selecting server in UI

        // Store connection without server tracking (server will be set via server.join message)
        var userConnection = new UserConnection
        {
            WebSocket = webSocket,
            Username = username,
            Role = role,
            ServerId = null,
            ConnectionId = connectionId
        };
        connections[username] = userConnection;
        Console.WriteLine($"User {username} connected (no server assigned yet). Total connections: {connections.Count}");

        // Create private queue for this user
        rabbitMqQueueManager.AddPrivateQueueForUserAsync(username).GetAwaiter().GetResult();

        // DON'T create server queue yet - will be created when user sends server.join message

        // Notify SessionService of login (without server)
        sessionServiceClient.LoginAsync(username, role, connectionId, null).GetAwaiter().GetResult();

        // Publish presence event to RabbitMQ (no server yet)
        PublishPresenceEventAsync("connected", username, role, null).GetAwaiter().GetResult();
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

        // If user was on a server, broadcast leave event to remaining users on that server
        if (!string.IsNullOrEmpty(serverId))
        {
            var leaveEvent = new ChatMessage
            {
                Type = "server.user.left",
                FromUsername = username,
                Role = role,
                ServerId = serverId,
                Timestamp = DateTime.UtcNow,
                Content = $"{username} disconnected"
            };

            // Publish leave event (fire and forget - user is disconnecting anyway)
            Task.Run(async () =>
            {
                try
                {
                    await rabbitMqPublisher.PublishChatMessageAsync("chat.server", $"server.{serverId}", leaveEvent);
                    Console.WriteLine($"Broadcasted disconnect leave event for {username} on server {serverId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error broadcasting leave event for {username}: {ex.Message}");
                }
            });
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
        var messageText = Encoding.UTF8.GetString(message);
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [RELAY→ALL.CLIENTS] Broadcasting to all | Message={messageText.Substring(0, Math.Min(100, messageText.Length))}...");

        var sentCount = 0;
        var totalConnections = connections.Count;

        // Iterate through all connections and send message to those that are open
        foreach (var userConnection in connections.Values)
        {
            if (userConnection.WebSocket.State == WebSocketState.Open)
            {
                try
                {
                    await userConnection.WebSocket.SendAsync(
                        new ArraySegment<byte>(message),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                    sentCount++;
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SENT] Message sent to user {userConnection.Username}");
                }
                catch (Exception exception)
                {
                    // Log error but continue broadcasting to other clients
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ERROR] Error sending to user {userConnection.Username}: {exception.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SKIP] User {userConnection.Username} WebSocket not open (state: {userConnection.WebSocket.State})");
            }
        }

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [BROADCAST.COMPLETE] Sent message to {sentCount}/{totalConnections} connected clients");
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
        var messageText = Encoding.UTF8.GetString(message);
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [RELAY→CLIENT] Target={username} | Message={messageText.Substring(0, Math.Min(100, messageText.Length))}...");

        // Check if user is connected
        if (connections.TryGetValue(username, out var userConnection))
        {
            if (userConnection.WebSocket.State == WebSocketState.Open)
            {
                try
                {
                    await userConnection.WebSocket.SendAsync(
                        new ArraySegment<byte>(message),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SENT] Successfully sent message to user {username}");
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ERROR] Error sending message to user {username}: {exception.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ERROR] WebSocket for user {username} is not in Open state (state: {userConnection.WebSocket.State})");
            }
        }
        else
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ERROR] User {username} is not connected - message not delivered");
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
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [RELAY→CLIENTS] Target=Server.{serverId} | Message={messageText.Substring(0, Math.Min(100, messageText.Length))}...");

        var sentCount = 0;
        var eligibleUsers = connections.Values.Where(c => c.ServerId == serverId).ToList();
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [BROADCAST] Found {eligibleUsers.Count} users on server {serverId}");

        foreach (var userConnection in eligibleUsers)
        {
            // Only send to users on the specified server
            if (userConnection.WebSocket.State == WebSocketState.Open)
            {
                try
                {
                    await userConnection.WebSocket.SendAsync(
                        new ArraySegment<byte>(message),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                    sentCount++;
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SENT] Message sent to user {userConnection.Username} on server {serverId}");
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ERROR] Error sending server message to user {userConnection.Username}: {exception.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SKIP] User {userConnection.Username} WebSocket not open (state: {userConnection.WebSocket.State})");
            }
        }

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [BROADCAST.COMPLETE] Sent server message to {sentCount}/{eligibleUsers.Count} users on server {serverId}");
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
    /// Updates the server assignment for a connected user.
    /// Creates server queue if joining a server, clears assignment if leaving.
    /// </summary>
    /// <param name="username">The username of the user to update</param>
    /// <param name="serverId">The server ID to assign (null to clear assignment)</param>
    public void UpdateUserServer(string username, string? serverId)
    {
        if (connections.TryGetValue(username, out var userConnection))
        {
            // Create new connection object with updated ServerId
            var updatedConnection = new UserConnection
            {
                WebSocket = userConnection.WebSocket,
                Username = userConnection.Username,
                Role = userConnection.Role,
                ConnectionId = userConnection.ConnectionId,
                ServerId = serverId
            };

            connections[username] = updatedConnection;

            // If joining a server, create server queue if it doesn't exist
            if (!string.IsNullOrEmpty(serverId))
            {
                rabbitMqQueueManager.AddServerQueueAsync(serverId).GetAwaiter().GetResult();
                Console.WriteLine($"Updated user {username} server assignment to {serverId}");
            }
            else
            {
                Console.WriteLine($"Cleared server assignment for user {username}");
            }
        }
        else
        {
            Console.WriteLine($"Cannot update server for {username} - user not connected");
        }
    }

    /// <summary>
    /// Gets the current server ID for a connected user.
    /// </summary>
    /// <param name="username">The username to look up</param>
    /// <returns>The server ID if user is on a server, null otherwise</returns>
    public string? GetUserServerId(string username)
    {
        if (connections.TryGetValue(username, out var userConnection))
        {
            return userConnection.ServerId;
        }
        return null;
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
