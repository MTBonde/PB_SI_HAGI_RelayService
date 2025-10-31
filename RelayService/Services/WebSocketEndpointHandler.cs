using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RelayService.Configuration;
using RelayService.Interfaces;
using RelayService.Models;

namespace RelayService.Services;

/// <summary>
/// Handles WebSocket endpoint logic including JWT authentication, connection establishment, and lifecycle management
/// </summary>
public class WebSocketEndpointHandler
{
    private readonly IJwtValidationService jwtValidationService;
    private readonly IWebSocketConnectionManager connectionManager;
    private readonly WebSocketConfiguration configuration;
    private readonly IRabbitMqPublisher rabbitMqPublisher;
    private readonly ISessionServiceClient sessionServiceClient;

    /// <summary>
    /// Initializes a new instance of the WebSocketEndpointHandler class with the specified dependencies
    /// </summary>
    /// <param name="jwtValidationService">Service for validating JWT tokens</param>
    /// <param name="connectionManager">Manager for WebSocket connections</param>
    /// <param name="configuration">WebSocket configuration settings</param>
    /// <param name="rabbitMqPublisher">RabbitMQ publisher for forwarding client messages</param>
    /// <param name="sessionServiceClient">SessionService client for retrieving user server assignments</param>
    public WebSocketEndpointHandler(
        IJwtValidationService jwtValidationService,
        IWebSocketConnectionManager connectionManager,
        WebSocketConfiguration configuration,
        IRabbitMqPublisher rabbitMqPublisher,
        ISessionServiceClient sessionServiceClient)
    {
        this.jwtValidationService = jwtValidationService;
        this.connectionManager = connectionManager;
        this.configuration = configuration;
        this.rabbitMqPublisher = rabbitMqPublisher;
        this.sessionServiceClient = sessionServiceClient;
    }

    /// <summary>
    /// Handles incoming WebSocket connection requests with JWT authentication
    /// </summary>
    /// <param name="context">The HTTP context containing the WebSocket request</param>
    /// <returns>A task representing the asynchronous connection handling operation</returns>
    /// <remarks>
    /// This method performs the following steps:
    /// 1. Validates that the request is a WebSocket upgrade request
    /// 2. Extracts and validates the JWT token from query string
    /// 3. Establishes the WebSocket connection
    /// 4. Manages the connection lifecycle until disconnection
    /// </remarks>
    public async Task HandleWebSocketConnectionAsync(HttpContext context)
    {
        // EO; request is not a WebSocket request
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        // Get JWT token from query string
        var token = context.Request.Query["token"].ToString();

        // EO; token is missing
        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Missing token");
            return;
        }

        // Validate JWT token and get claims principal
        if (!jwtValidationService.TryValidateTokenAndGetPrincipal(token, out var principal))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid token");
            return;
        }

        // Extract user ID from validated principal (accepts multiple claim type aliases)
        var userId = principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("nameid")?.Value ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? Guid.NewGuid().ToString(); // v0.2 workaround: Auth service doesn't send nameid yet


        // Extract username
        var username = principal.FindFirst(ClaimTypes.Name)?.Value ?? principal.FindFirst("Username")?.Value ?? "UnknownUser";

        // Extract role
        var role = principal.FindFirst(ClaimTypes.Role)?.Value ?? principal.FindFirst("Role")?.Value ?? "Player"; 

        // Accept WebSocket connection
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        // Add connection to manager
        connectionManager.AddConnection(username, role, webSocket);

        // Send welcome message
        await connectionManager.SendWelcomeMessageAsync(webSocket);

        // Keep connection alive and handle messages
        await HandleWebSocketLifecycleAsync(webSocket, userId, username, role);
    }

    /// <summary>
    /// Manages the WebSocket connection lifecycle, keeping it alive and handling closure
    /// </summary>
    /// <param name="webSocket">The WebSocket connection to manage</param>
    /// <param name="userId">The user ID associated with this connection</param>
    /// <returns>A task representing the asynchronous lifecycle management</returns>
    /// <remarks>
    /// This method runs in a loop receiving messages until the connection is closed.
    /// Received text messages are parsed and forwarded to RabbitMQ.
    /// Connection is removed from the manager in the finally block to ensure cleanup.
    /// </remarks>
    private async Task HandleWebSocketLifecycleAsync(WebSocket webSocket, string userId, string username, string role)
    {
        var buffer = new byte[configuration.BufferSize];

        try
        {
            // Keep connection alive while it's open
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None);

                // Handle close message from client
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing",
                        CancellationToken.None);
                    break;
                }

                // Handle text messages from client
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    await ProcessClientMessageAsync(buffer, result.Count, username, role);
                }
            }
        }
        catch (WebSocketException exception)
        {
            Console.WriteLine($"WebSocket error for user {username}: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unexpected error for user {username}: {exception.Message}");
        }
        finally
        {
            // Always remove connection on disconnect to prevent memory leaks
            connectionManager.RemoveConnection(username, role);
        }
    }

    /// <summary>
    /// Processes incoming client messages and forwards them to RabbitMQ.
    /// Routes messages based on type: global (broadcast), private (user-specific), or server (server-group).
    /// </summary>
    /// <param name="buffer">The buffer containing the message bytes</param>
    /// <param name="count">The number of bytes in the message</param>
    /// <param name="username">The username of the sender</param>
    /// <param name="role">The role of the sender</param>
    private async Task ProcessClientMessageAsync(byte[] buffer, int count, string username, string role)
    {
        try
        {
            // Convert bytes to string
            var messageText = Encoding.UTF8.GetString(buffer, 0, count);
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [CLIENT→RELAY] Raw message from {username}: {messageText}");

            // EO; Parse message as ChatMessage
            var chatMessage = JsonSerializer.Deserialize<ChatMessage>(messageText);
            if (chatMessage == null)
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ERROR] Failed to deserialize message from {username}");
                return;
            }

            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [PARSED] Type={chatMessage.Type} | From={username} | To={chatMessage.ToUsername ?? "N/A"} | Server={chatMessage.ServerId ?? "N/A"} | Content={(string.IsNullOrEmpty(chatMessage.Content) ? "N/A" : chatMessage.Content.Length > 50 ? chatMessage.Content.Substring(0, 50) + "..." : chatMessage.Content)}");

            // EO; Default to global if type not specified
            if (string.IsNullOrEmpty(chatMessage.Type))
            {
                Console.WriteLine($"Chat defaulted to global {username}");
                chatMessage.Type = "chat.global";
            }

            // Enrich message with server-side data
            chatMessage.FromUsername = username;
            chatMessage.Role = role;
            chatMessage.Timestamp = DateTime.UtcNow;

            // Route based on message type
            switch (chatMessage.Type.ToLower())
            {
                case "chat.private":
                    // Private messages - route to specific user
                    await HandlePrivateMessageAsync(chatMessage, username);
                    break;

                case "chat.server":
                    // Server messages - route to users on same server
                    await HandleServerMessageAsync(chatMessage, username);
                    break;

                case "chat.global":
                    // Global messages - broadcast to all
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ROUTING→GLOBAL] Type={chatMessage.Type} | From={username} | Content={(string.IsNullOrEmpty(chatMessage.Content) ? "N/A" : chatMessage.Content.Length > 50 ? chatMessage.Content.Substring(0, 50) + "..." : chatMessage.Content)}");
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [PUBLISH] Exchange=chat.global | RoutingKey=(empty) | From={username}");
                    await rabbitMqPublisher.PublishChatMessageAsync("chat.global", "", chatMessage);
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [PUBLISHED] Global message from {username} to chat.global");
                    break;

                case "server.join":
                    // User joins a server
                    await HandleServerJoinAsync(chatMessage, username, role);
                    break;

                case "server.leave":
                    // User leaves a server
                    await HandleServerLeaveAsync(chatMessage, username, role);
                    break;

                default:
                    Console.WriteLine($"Unknown message type from {username}: {chatMessage.Type}");
                    break;
            }
        }
        catch (JsonException exception)
        {
            Console.WriteLine($"JSON parsing error for message from {username}: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error processing message from {username}: {exception.Message}");
        }
    }

    /// <summary>
    /// Handles private message routing to a specific user.
    /// Validates ToUsername is provided and publishes to chat.private exchange with user-specific routing key.
    /// </summary>
    /// <param name="chatMessage">The chat message to send</param>
    /// <param name="senderUsername">The username of the sender</param>
    private async Task HandlePrivateMessageAsync(ChatMessage chatMessage, string senderUsername)
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ROUTING→PRIVATE] Type={chatMessage.Type} | From={senderUsername} | To={chatMessage.ToUsername ?? "MISSING"} | Content={(string.IsNullOrEmpty(chatMessage.Content) ? "N/A" : chatMessage.Content.Length > 50 ? chatMessage.Content.Substring(0, 50) + "..." : chatMessage.Content)}");

        // Validate ToUsername is provided
        if (string.IsNullOrEmpty(chatMessage.ToUsername))
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ERROR] Private message from {senderUsername} missing ToUsername - message ignored");
            return;
        }

        // Publish to chat.private exchange with user-specific routing key
        var routingKey = $"user.{chatMessage.ToUsername}";
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [PUBLISH] Exchange=chat.private | RoutingKey={routingKey} | From={senderUsername} | To={chatMessage.ToUsername}");
        await rabbitMqPublisher.PublishChatMessageAsync("chat.private", routingKey, chatMessage);

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [PUBLISHED] Private message from {senderUsername} to {chatMessage.ToUsername} via chat.private exchange");
    }

    /// <summary>
    /// Handles server message routing to all users on the same server.
    /// Retrieves sender's server ID and publishes to chat.server exchange with server-specific routing key.
    /// </summary>
    /// <param name="chatMessage">The chat message to send</param>
    /// <param name="senderUsername">The username of the sender</param>
    private async Task HandleServerMessageAsync(ChatMessage chatMessage, string senderUsername)
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ROUTING→SERVER] Type={chatMessage.Type} | From={senderUsername} | Server={chatMessage.ServerId ?? "LOOKUP"} | Content={(string.IsNullOrEmpty(chatMessage.Content) ? "N/A" : chatMessage.Content.Length > 50 ? chatMessage.Content.Substring(0, 50) + "..." : chatMessage.Content)}");

        // Get server ID - either from message or from SessionService
        var serverId = chatMessage.ServerId;
        if (string.IsNullOrEmpty(serverId))
        {
            serverId = await sessionServiceClient.GetServerIdAsync(senderUsername);
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [LOOKUP] Fetched serverId from SessionService for {senderUsername}: {serverId ?? "NULL"}");
        }

        // Validate server ID is available
        if (string.IsNullOrEmpty(serverId))
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ERROR] Server message from {senderUsername} has no ServerId - message ignored");
            return;
        }

        // Publish to chat.server exchange with server-specific routing key
        var routingKey = $"server.{serverId}";
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [PUBLISH] Exchange=chat.server | RoutingKey={routingKey} | From={senderUsername} | Server={serverId}");
        await rabbitMqPublisher.PublishChatMessageAsync("chat.server", routingKey, chatMessage);

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [PUBLISHED] Server message from {senderUsername} to server {serverId} via chat.server exchange");
    }

    /// <summary>
    /// Handles server join requests from clients.
    /// Updates SessionService, internal state, creates server queue, and broadcasts join event.
    /// </summary>
    /// <param name="message">The server join message containing ServerId</param>
    /// <param name="username">The username of the user joining the server</param>
    /// <param name="role">The role of the user joining the server</param>
    private async Task HandleServerJoinAsync(ChatMessage message, string username, string role)
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SERVER.JOIN] User={username} | RequestedServer={message.ServerId ?? "MISSING"}");

        // Validate ServerId is provided
        if (string.IsNullOrEmpty(message.ServerId))
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ERROR] server.join from {username} missing ServerId - message ignored");
            return;
        }

        var serverId = message.ServerId;
        var previousServerId = connectionManager.GetUserServerId(username);

        // If already on a server, handle implicit leave before joining new server
        if (!string.IsNullOrEmpty(previousServerId) && previousServerId != serverId)
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SERVER.SWITCH] User={username} | From={previousServerId} | To={serverId}");
            await HandleServerLeaveInternalAsync(username, role, previousServerId);
        }

        // Update SessionService with new server assignment
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SESSION.UPDATE] Updating SessionService for {username} to server {serverId}");
        await sessionServiceClient.UpdateServerAsync(username, serverId);

        // Update internal connection state (this also creates server queue if needed)
        connectionManager.UpdateUserServer(username, serverId);

        // Broadcast join event to all users on this server
        var joinEvent = new ChatMessage
        {
            Type = "server.user.joined",
            FromUsername = username,
            Role = role,
            ServerId = serverId,
            Timestamp = DateTime.UtcNow,
            Content = $"{username} joined the server"
        };

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [PUBLISH] Exchange=chat.server | RoutingKey=server.{serverId} | Event=user.joined | User={username}");
        await rabbitMqPublisher.PublishChatMessageAsync("chat.server", $"server.{serverId}", joinEvent);

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SERVER.JOIN.SUCCESS] User {username} joined server {serverId} successfully");
    }

    /// <summary>
    /// Handles explicit server leave requests from clients.
    /// </summary>
    /// <param name="message">The server leave message</param>
    /// <param name="username">The username of the user leaving the server</param>
    /// <param name="role">The role of the user leaving the server</param>
    private async Task HandleServerLeaveAsync(ChatMessage message, string username, string role)
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SERVER.LEAVE] User={username} requested to leave server");

        var currentServerId = connectionManager.GetUserServerId(username);

        // Validate user is actually on a server
        if (string.IsNullOrEmpty(currentServerId))
        {
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [ERROR] server.leave from {username} but user is not on any server - message ignored");
            return;
        }

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SERVER.LEAVE] User={username} leaving server {currentServerId}");
        await HandleServerLeaveInternalAsync(username, role, currentServerId);
    }

    /// <summary>
    /// Internal method to handle server leave logic.
    /// Used by both explicit leave requests and implicit leaves during server switching.
    /// </summary>
    /// <param name="username">The username of the user leaving the server</param>
    /// <param name="role">The role of the user leaving the server</param>
    /// <param name="serverId">The server ID the user is leaving from</param>
    private async Task HandleServerLeaveInternalAsync(string username, string role, string serverId)
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SERVER.LEAVE.INTERNAL] User={username} | Server={serverId}");

        // Update SessionService to clear server assignment
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SESSION.UPDATE] Clearing SessionService server assignment for {username}");
        await sessionServiceClient.UpdateServerAsync(username, null);

        // Update internal connection state
        connectionManager.UpdateUserServer(username, null);

        // Broadcast leave event to remaining users on the server
        var leaveEvent = new ChatMessage
        {
            Type = "server.user.left",
            FromUsername = username,
            Role = role,
            ServerId = serverId,
            Timestamp = DateTime.UtcNow,
            Content = $"{username} left the server"
        };

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [PUBLISH] Exchange=chat.server | RoutingKey=server.{serverId} | Event=user.left | User={username}");
        await rabbitMqPublisher.PublishChatMessageAsync("chat.server", $"server.{serverId}", leaveEvent);

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [SERVER.LEAVE.SUCCESS] User {username} left server {serverId}");
    }
}
