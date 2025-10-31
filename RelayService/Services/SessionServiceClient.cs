using System.Text;
using System.Text.Json;
using RelayService.Interfaces;

namespace RelayService.Services;

/// <summary>
/// HTTP client for communicating with the SessionService REST API for tracking user online status and server assignments
/// </summary>
public class SessionServiceClient : ISessionServiceClient
{
    private readonly HttpClient httpClient;
    private readonly string baseUrl;

    /// <summary>
    /// Initializes a new instance of the SessionServiceClient with the specified HTTP client
    /// </summary>
    /// <param name="httpClient">HTTP client for making requests to SessionService</param>
    /// <param name="sessionServiceUrl">Base URL of the SessionService (e.g., http://sessionservice:8081)</param>
    public SessionServiceClient(HttpClient httpClient, string sessionServiceUrl)
    {
        this.httpClient = httpClient;
        this.baseUrl = sessionServiceUrl.TrimEnd('/');
    }

    /// <summary>
    /// Registers a user as online with their current connection and server
    /// </summary>
    /// <param name="username">The username of the user logging in</param>
    /// <param name="role">The role of the user (from JWT claims)</param>
    /// <param name="connectionId">The connection identifier for this WebSocket session</param>
    /// <param name="serverId">Optional server ID where the user is connected</param>
    public async Task LoginAsync(string username, string role, string connectionId, string? serverId = null)
    {
        try
        {
            var loginRequest = new
            {
                username = username,
                role = role,
                connectionId = connectionId,
                serverId = serverId
            };

            var json = JsonSerializer.Serialize(loginRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{baseUrl}/online/login", content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"SessionService: User {username} logged in successfully (server: {serverId ?? "none"})");
            }
            else
            {
                Console.WriteLine($"SessionService: Failed to log in user {username} - Status: {response.StatusCode}");
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"SessionService: Error logging in user {username}: {exception.Message}");
        }
    }

    /// <summary>
    /// Registers a user as offline, removing them from the online user list
    /// </summary>
    /// <param name="username">The username of the user logging out</param>
    /// <param name="connectionId">The connection identifier being closed</param>
    public async Task LogoutAsync(string username, string connectionId)
    {
        try
        {
            var logoutRequest = new
            {
                username = username,
                connectionId = connectionId
            };

            var json = JsonSerializer.Serialize(logoutRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{baseUrl}/online/logout", content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"SessionService: User {username} logged out successfully");
            }
            else
            {
                Console.WriteLine($"SessionService: Failed to log out user {username} - Status: {response.StatusCode}");
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"SessionService: Error logging out user {username}: {exception.Message}");
        }
    }

    /// <summary>
    /// Retrieves the server ID where a specific user is currently connected
    /// </summary>
    /// <param name="username">The username to look up</param>
    /// <returns>The server ID where the user is connected, or null if not found or not on a server</returns>
    public async Task<string?> GetServerIdAsync(string username)
    {
        try
        {
            var response = await httpClient.GetAsync($"{baseUrl}/online/{username}");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var userInfo = JsonSerializer.Deserialize<OnlineUserInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                Console.WriteLine($"SessionService: User {username} is on server {userInfo?.ServerId ?? "none"}");
                return userInfo?.ServerId;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.WriteLine($"SessionService: User {username} not found (offline)");
                return null;
            }
            else
            {
                Console.WriteLine($"SessionService: Failed to get server for user {username} - Status: {response.StatusCode}");
                return null;
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"SessionService: Error getting server for user {username}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Updates the server assignment for an online user
    /// </summary>
    /// <param name="username">The username of the user to update</param>
    /// <param name="serverId">The new server ID to assign (null to clear server assignment)</param>
    public async Task UpdateServerAsync(string username, string? serverId)
    {
        try
        {
            var updateRequest = new
            {
                username = username,
                serverId = serverId
            };

            var json = JsonSerializer.Serialize(updateRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{baseUrl}/online/set-server", content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"SessionService: Updated server assignment for {username} to {serverId ?? "none"}");
            }
            else
            {
                Console.WriteLine($"SessionService: Failed to update server for {username} - Status: {response.StatusCode}");
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"SessionService: Error updating server for {username}: {exception.Message}");
        }
    }

    /// <summary>
    /// Represents online user information returned from SessionService
    /// </summary>
    private class OnlineUserInfo
    {
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string? ServerId { get; set; }
        public string ConnectionId { get; set; } = string.Empty;
    }
}
