namespace RelayService;

/// <summary>
/// Contains the current version of the RelayService
/// </summary>
public static class ServiceVersion
{
    /// <summary>
    /// Gets the current version of the RelayService in semantic versioning format (major.minor.patch)
    /// </summary>
    public const string Current = "0.5.3";
}

// Version History:
// 0.1.0 - walking skeleton
// 0.2.0 - websocket support
// 0.2.1 - dockerfile fix
// 0.2.2 - token adaption
// 0.3.0 - relay added (bidirectional RabbitMQ)
// 0.4.0 - relay 4.0
// 0.4.1 - minor fixes
// 0.5.0 - chat refactor Phase 1 (chat.global exchange, ChatMessage model, basic message loop working)
// 0.5.1 - chat refactor Phase 2 (chat.private and chat.server)
// 0.5.2 - fixed circular dependency error
// 0.5.3 - fixing chat.server and server logging in session