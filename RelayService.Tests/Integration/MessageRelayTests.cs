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

        var chatMessage = new ChatMessage
        {
            Type = "global",
            Content = "Hello from integration test!",
            Timestamp = DateTime.UtcNow
        };

        var messageJson = JsonSerializer.Serialize(chatMessage);
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

        var publishedMessage = publishedMessages.FirstOrDefault(
            message => message.Exchange == "chat.global");

        Assert.IsNotNull(publishedMessage, "Message should be published to chat.global exchange");
        Assert.IsTrue(publishedMessage.Message.Contains("Hello from integration test!"),
            "Published message should contain the original content");
        Assert.IsTrue(publishedMessage.Message.Contains("global"),
            "Published message should contain the message type");

        // Cleanup
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test completed",
            CancellationToken.None);
        webSocket.Dispose();
    }

}
