using System.Net.WebSockets;

namespace RelayService.Interfaces;

/// <summary>
/// Manages WebSocket connections lifecycle including adding, removing, and broadcasting messages to connected clients
/// </summary>
public interface IWebSocketConnectionManager
{
    /// <summary>
    /// Adds a new WebSocket connection to the connection pool and logs the connection event
    /// </summary>
    /// <param name="userId">The unique identifier for the user</param>
    /// <param name="webSocket">The WebSocket connection to add</param>
    void AddConnection(string userId, string role, WebSocket webSocket);

    /// <summary>
    /// Removes a WebSocket connection from the connection pool and logs the disconnection event
    /// </summary>
    /// <param name="userId">The unique identifier for the user to disconnect</param>
    void RemoveConnection(string userId, string role);

    /// <summary>
    /// Sends a welcome message to a newly connected WebSocket client
    /// </summary>
    /// <param name="webSocket">The WebSocket connection to send the message to</param>
    /// <returns>A task representing the asynchronous send operation</returns>
    Task SendWelcomeMessageAsync(WebSocket webSocket);

    /// <summary>
    /// Broadcasts a message to all connected WebSocket clients
    /// </summary>
    /// <param name="message">The message bytes to broadcast</param>
    /// <returns>A task representing the asynchronous broadcast operation</returns>
    /// <remarks>Messages are only sent to connections in the Open state. Errors during send are logged but do not stop the broadcast to other clients</remarks>
    Task BroadcastMessageAsync(byte[] message);

    /// <summary>
    /// Gets the current number of active WebSocket connections
    /// </summary>
    /// <returns>The count of active connections</returns>
    int GetConnectionCount();
}
