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

    /// <summary>
    /// Initializes a new instance of the WebSocketEndpointHandler class with the specified dependencies
    /// </summary>
    /// <param name="jwtValidationService">Service for validating JWT tokens</param>
    /// <param name="connectionManager">Manager for WebSocket connections</param>
    /// <param name="configuration">WebSocket configuration settings</param>
    /// <param name="rabbitMqPublisher">RabbitMQ publisher for forwarding client messages</param>
    public WebSocketEndpointHandler(
        IJwtValidationService jwtValidationService,
        IWebSocketConnectionManager connectionManager,
        WebSocketConfiguration configuration,
        IRabbitMqPublisher rabbitMqPublisher)
    {
        this.jwtValidationService = jwtValidationService;
        this.connectionManager = connectionManager;
        this.configuration = configuration;
        this.rabbitMqPublisher = rabbitMqPublisher;
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
        var userId = principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? principal.FindFirst("nameid")?.Value
                   ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                   ?? Guid.NewGuid().ToString(); // v0.2 workaround: Auth service doesn't send nameid yet

        // TODO v0.3: Uncomment when Auth service includes nameid claim
        // EO; user ID not found in token claims
        // if (string.IsNullOrEmpty(userId))
        // {
        //     context.Response.StatusCode = 401;
        //     await context.Response.WriteAsync("User ID not found in token");
        //     return;
        // }
        
        // Extract username
        var username = principal.FindFirst(ClaimTypes.Name)?.Value
                       ?? principal.FindFirst("Username")?.Value
                       ?? "UnknownUser";

        // Extract role
        var role = principal.FindFirst(ClaimTypes.Role)?.Value
                   ?? principal.FindFirst("Role")?.Value
                   ?? "Player"; 

        // Accept WebSocket connection
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        // Add connection to manager
        connectionManager.AddConnection(userId, role, webSocket);

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
    /// Processes incoming client messages and forwards them to RabbitMQ
    /// </summary>
    /// <param name="buffer">The buffer containing the message bytes</param>
    /// <param name="count">The number of bytes in the message</param>
    /// <param name="userId">The user ID of the sender</param>
    /// <param name="username"></param>
    /// <param name="role"></param>
    private async Task ProcessClientMessageAsync(byte[] buffer, int count, string username, string role)
    {
        try
        {
            // Convert bytes to string
            var messageText = Encoding.UTF8.GetString(buffer, 0, count);
            Console.WriteLine($"Received message from user {username}: {messageText}");

            // Parse message
            var relayMessage = JsonSerializer.Deserialize<RelayMessage>(messageText);
            if (relayMessage == null) return;

            // Enrich with user info
            relayMessage.Username = username;
            relayMessage.Role = role;

            // For now, forward all messages to relay.chat.global (fanout exchange)
            // Simple KISS approach - no complex routing yet
            var enrichedMessage = JsonSerializer.Serialize(relayMessage);
            await rabbitMqPublisher.PublishAsync("relay.chat.global", "", enrichedMessage);

            Console.WriteLine($"Forwarded message from user {username} to RabbitMQ");
        }
        catch (JsonException exception)
        {
            Console.WriteLine($"JSON parsing error for message from user {username}: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error processing message from user {username}: {exception.Message}");
        }
    }
}
