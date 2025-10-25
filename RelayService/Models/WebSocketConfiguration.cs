namespace RelayService.Models;

/// <summary>
/// Configuration settings for WebSocket connections and message handling
/// </summary>
public class WebSocketConfiguration
{
    /// <summary>
    /// Gets or sets the buffer size in bytes for receiving WebSocket messages
    /// </summary>
    /// <remarks>Default is 4096 bytes (4 KB). Increase for larger message payloads</remarks>
    public int BufferSize { get; set; } = 1024 * 4;

    /// <summary>
    /// Gets or sets the welcome message sent to clients upon successful WebSocket connection
    /// </summary>
    public string WelcomeMessage { get; set; } = "Welcome to WEBSOCKET!";
}
