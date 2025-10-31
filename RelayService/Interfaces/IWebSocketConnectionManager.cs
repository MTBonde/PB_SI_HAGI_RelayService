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
    /// Sends a message to a specific user by username
    /// </summary>
    /// <param name="username">The username of the recipient</param>
    /// <param name="message">The message bytes to send</param>
    /// <returns>A task representing the asynchronous send operation</returns>
    /// <remarks>Message is only sent if the user is connected and their WebSocket is in Open state</remarks>
    Task SendMessageToUserAsync(string username, byte[] message);

    /// <summary>
    /// Broadcasts a message to all users connected to a specific server
    /// </summary>
    /// <param name="serverId">The server ID to broadcast to</param>
    /// <param name="message">The message bytes to send</param>
    /// <returns>A task representing the asynchronous broadcast operation</returns>
    /// <remarks>Only sends to users whose ServerId matches the specified server</remarks>
    Task BroadcastMessageToServerAsync(string serverId, byte[] message);

    /// <summary>
    /// Gets the current number of active WebSocket connections
    /// </summary>
    /// <returns>The count of active connections</returns>
    int GetConnectionCount();

    /// <summary>
    /// Updates the server assignment for a connected user
    /// </summary>
    /// <param name="username">The username of the user to update</param>
    /// <param name="serverId">The server ID to assign (null to clear assignment)</param>
    void UpdateUserServer(string username, string? serverId);

    /// <summary>
    /// Gets the current server ID for a connected user
    /// </summary>
    /// <param name="username">The username to look up</param>
    /// <returns>The server ID if user is on a server, null otherwise</returns>
    string? GetUserServerId(string username);
}
