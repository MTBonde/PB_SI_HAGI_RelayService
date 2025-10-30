namespace RelayService.Interfaces;

/// <summary>
/// Client for communicating with the SessionService to track user online status and server assignments
/// </summary>
public interface ISessionServiceClient
{
    /// <summary>
    /// Registers a user as online with their current connection and server
    /// </summary>
    /// <param name="username">The username of the user logging in</param>
    /// <param name="role">The role of the user (from JWT claims)</param>
    /// <param name="connectionId">The connection identifier for this WebSocket session</param>
    /// <param name="serverId">Optional server ID where the user is connected</param>
    /// <returns>A task representing the asynchronous login operation</returns>
    Task LoginAsync(string username, string role, string connectionId, string? serverId = null);

    /// <summary>
    /// Registers a user as offline, removing them from the online user list
    /// </summary>
    /// <param name="username">The username of the user logging out</param>
    /// <param name="connectionId">The connection identifier being closed</param>
    /// <returns>A task representing the asynchronous logout operation</returns>
    Task LogoutAsync(string username, string connectionId);

    /// <summary>
    /// Retrieves the server ID where a specific user is currently connected
    /// </summary>
    /// <param name="username">The username to look up</param>
    /// <returns>The server ID where the user is connected, or null if not found or not on a server</returns>
    Task<string?> GetServerIdAsync(string username);
}
