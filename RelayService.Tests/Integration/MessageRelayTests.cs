using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RelayService.Models;
using RelayService.Tests.Helpers;

namespace RelayService.Tests.Integration;

/// <summary>
/// Integration tests for bidirectional message relay functionality
/// Tests use the AAA (Arrange-Act-Assert) pattern
/// </summary>
[TestClass]
public class MessageRelayTests
{
    private TestWebApplicationFactory? webApplicationFactory;

    [TestInitialize]
    public void TestInitialize()
    {
        webApplicationFactory = new TestWebApplicationFactory();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        webApplicationFactory?.Dispose();
    }

    [TestMethod]
    public async Task ClientSendsMessage_ShouldForwardToRabbitMq()
    {
        // Arrange
        var validToken = TestJwtTokenGenerator.GenerateValidToken(
            userId: "test-user-456",
            username: "TestSender");

        var webSocketClient = webApplicationFactory!.Server.CreateWebSocketClient();
        var baseUri = webApplicationFactory.Server.BaseAddress;
        var wsUri = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = "/ws",
            Query = $"token={Uri.EscapeDataString(validToken)}"
        }.Uri;

        var relayMessage = new RelayMessage
        {
            Type = "chat.global",
            Content = "Hello from integration test!",
            Timestamp = DateTime.UtcNow
        };

        var messageJson = JsonSerializer.Serialize(relayMessage);
        var messageBytes = Encoding.UTF8.GetBytes(messageJson);

        // Act
        var webSocket = await webSocketClient.ConnectAsync(wsUri, CancellationToken.None);

        // Receive welcome message first
        var welcomeBuffer = new byte[1024];
        await webSocket.ReceiveAsync(new ArraySegment<byte>(welcomeBuffer), CancellationToken.None);

        // Clear any presence events from connection
        webApplicationFactory.MockPublisher!.ClearPublishedMessages();

        // Send message to server
        await webSocket.SendAsync(
            new ArraySegment<byte>(messageBytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

        // Wait a bit for message to be processed
        await Task.Delay(100);

        // Assert
        var publishedMessages = webApplicationFactory.MockPublisher!.GetPublishedMessages();

        Assert.IsTrue(publishedMessages.Count > 0, "At least one message should be published");

        var chatMessage = publishedMessages.FirstOrDefault(
            message => message.Exchange == "relay.chat.global");

        Assert.IsNotNull(chatMessage, "Message should be published to relay.chat.global exchange");
        Assert.IsTrue(chatMessage.Message.Contains("Hello from integration test!"),
            "Published message should contain the original content");
        Assert.IsTrue(chatMessage.Message.Contains("chat.global"),
            "Published message should contain the message type");

        // Cleanup
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test completed",
            CancellationToken.None);
        webSocket.Dispose();
    }

    [TestMethod]
    public async Task ClientConnects_ShouldPublishPresenceEvent()
    {
        // Arrange
        var validToken = TestJwtTokenGenerator.GenerateValidToken(
            userId: "test-user-789",
            username: "PresenceTestUser");

        var webSocketClient = webApplicationFactory!.Server.CreateWebSocketClient();
        var baseUri = webApplicationFactory.Server.BaseAddress;
        var wsUri = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = "/ws",
            Query = $"token={Uri.EscapeDataString(validToken)}"
        }.Uri;

        // Clear any previous messages
        webApplicationFactory.MockPublisher!.ClearPublishedMessages();

        // Act
        var webSocket = await webSocketClient.ConnectAsync(wsUri, CancellationToken.None);

        // Wait for presence event to be published
        await Task.Delay(100);

        // Assert
        var publishedMessages = webApplicationFactory.MockPublisher!.GetPublishedMessages();

        Assert.IsTrue(publishedMessages.Count > 0, "Presence event should be published");

        var presenceEvent = publishedMessages.FirstOrDefault(
            message => message.Exchange == "relay.session.events" &&
                      message.RoutingKey.Contains("connected"));

        Assert.IsNotNull(presenceEvent, "Connected presence event should be published");
        Assert.IsTrue(presenceEvent.Message.Contains("connected"),
            "Presence event should contain 'connected' event type");

        // Cleanup
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test completed",
            CancellationToken.None);
        webSocket.Dispose();
    }

    [TestMethod]
    public async Task ClientDisconnects_ShouldPublishPresenceEvent()
    {
        // Arrange
        var validToken = TestJwtTokenGenerator.GenerateValidToken(
            userId: "test-user-disconnect",
            username: "DisconnectTestUser");

        var webSocketClient = webApplicationFactory!.Server.CreateWebSocketClient();
        var baseUri = webApplicationFactory.Server.BaseAddress;
        var wsUri = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = "/ws",
            Query = $"token={Uri.EscapeDataString(validToken)}"
        }.Uri;

        var webSocket = await webSocketClient.ConnectAsync(wsUri, CancellationToken.None);

        // Wait for connection to be established
        await Task.Delay(100);

        // Clear connection presence event
        webApplicationFactory.MockPublisher!.ClearPublishedMessages();

        // Act
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test disconnect",
            CancellationToken.None);

        // Wait for disconnect event to be published
        await Task.Delay(100);

        // Assert
        var publishedMessages = webApplicationFactory.MockPublisher!.GetPublishedMessages();

        Assert.IsTrue(publishedMessages.Count > 0, "Disconnect presence event should be published");

        var disconnectEvent = publishedMessages.FirstOrDefault(
            message => message.Exchange == "relay.session.events" &&
                      message.RoutingKey.Contains("disconnected"));

        Assert.IsNotNull(disconnectEvent, "Disconnected presence event should be published");
        Assert.IsTrue(disconnectEvent.Message.Contains("disconnected"),
            "Presence event should contain 'disconnected' event type");

        // Cleanup
        webSocket.Dispose();
    }

    [TestMethod]
    public async Task ClientSendsInvalidJson_ShouldNotCrashServer()
    {
        // Arrange
        var validToken = TestJwtTokenGenerator.GenerateValidToken(
            userId: "test-user-invalid",
            username: "InvalidJsonUser");

        var webSocketClient = webApplicationFactory!.Server.CreateWebSocketClient();
        var baseUri = webApplicationFactory.Server.BaseAddress;
        var wsUri = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = "/ws",
            Query = $"token={Uri.EscapeDataString(validToken)}"
        }.Uri;

        var invalidJson = "{ this is not valid json }";
        var messageBytes = Encoding.UTF8.GetBytes(invalidJson);

        // Act
        var webSocket = await webSocketClient.ConnectAsync(wsUri, CancellationToken.None);

        // Receive welcome message first
        var welcomeBuffer = new byte[1024];
        await webSocket.ReceiveAsync(new ArraySegment<byte>(welcomeBuffer), CancellationToken.None);

        // Send invalid JSON
        await webSocket.SendAsync(
            new ArraySegment<byte>(messageBytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

        // Wait a bit for message to be processed
        await Task.Delay(100);

        // Assert
        Assert.AreEqual(WebSocketState.Open, webSocket.State,
            "WebSocket should remain open even after invalid JSON");

        // Cleanup
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test completed",
            CancellationToken.None);
        webSocket.Dispose();
    }
}
