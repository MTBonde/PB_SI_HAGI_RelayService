using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
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

    /// <summary>
    /// Initializes a new instance of the WebSocketEndpointHandler class with the specified dependencies
    /// </summary>
    /// <param name="jwtValidationService">Service for validating JWT tokens</param>
    /// <param name="connectionManager">Manager for WebSocket connections</param>
    /// <param name="configuration">WebSocket configuration settings</param>
    public WebSocketEndpointHandler(
        IJwtValidationService jwtValidationService,
        IWebSocketConnectionManager connectionManager,
        WebSocketConfiguration configuration)
    {
        this.jwtValidationService = jwtValidationService;
        this.connectionManager = connectionManager;
        this.configuration = configuration;
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

        // Accept WebSocket connection
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        // Add connection to manager
        connectionManager.AddConnection(userId, webSocket);

        // Send welcome message
        await connectionManager.SendWelcomeMessageAsync(webSocket);

        // Keep connection alive and handle messages
        await HandleWebSocketLifecycleAsync(webSocket, userId);
    }

    /// <summary>
    /// Manages the WebSocket connection lifecycle, keeping it alive and handling closure
    /// </summary>
    /// <param name="webSocket">The WebSocket connection to manage</param>
    /// <param name="userId">The user ID associated with this connection</param>
    /// <returns>A task representing the asynchronous lifecycle management</returns>
    /// <remarks>
    /// This method runs in a loop receiving messages until the connection is closed.
    /// Currently, received messages are not processed - the connection is only kept alive.
    /// Connection is removed from the manager in the finally block to ensure cleanup.
    /// </remarks>
    private async Task HandleWebSocketLifecycleAsync(WebSocket webSocket, string userId)
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
            }
        }
        catch (WebSocketException exception)
        {
            Console.WriteLine($"WebSocket error for user {userId}: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Unexpected error for user {userId}: {exception.Message}");
        }
        finally
        {
            // Always remove connection on disconnect to prevent memory leaks
            connectionManager.RemoveConnection(userId);
        }
    }
}
