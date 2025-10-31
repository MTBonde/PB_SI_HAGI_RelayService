namespace RelayService.Models;

/// <summary>
/// Represents a chat message that can be routed through the relay service
/// </summary>
/// <remarks>
/// Supports three message types:
/// - "global": Broadcast to all connected users
/// - "private": Direct message to a specific user (requires ToUsername)
/// - "server": Message to all users on the same game server (requires ServerId)
/// </remarks>
public class ChatMessage
{
    /// <summary>
    /// Gets or sets the message type: "private", "server", or "global" og "server.something"
    /// </summary>
    public string Type { get; set; } = "global";

    /// <summary>
    /// Gets or sets the username of the sender
    /// </summary>
    /// <remarks>Set by the server based on JWT claims when message is received</remarks>
    public string FromUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username of the recipient for private messages
    /// </summary>
    /// <remarks>Only used when Type is "private"</remarks>
    public string? ToUsername { get; set; }

    /// <summary>
    /// Gets or sets the server identifier for server-group messages
    /// </summary>
    /// <remarks>Only used when Type is "server". If omitted, the sender's current server is used.</remarks>
    public string? ServerId { get; set; }

    /// <summary>
    /// Gets or sets the message content
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the role of the sender (e.g., "admin", "player")
    /// </summary>
    /// <remarks>Set by the server based on JWT claims when message is received</remarks>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the message was received by the server
    /// </summary>
    /// <remarks>Set by the server in UTC when message is received</remarks>
    public DateTime Timestamp { get; set; }
}
