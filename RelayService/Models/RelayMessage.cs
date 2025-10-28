namespace RelayService.Models;

public class RelayMessage
{
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
