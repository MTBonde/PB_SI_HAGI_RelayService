using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using RelayService.Tests.Helpers;

namespace RelayService.Tests.Integration;

/// <summary>
/// Integration tests for WebSocket connections to the RelayService
/// Tests use the AAA (Arrange-Act-Assert) pattern
/// </summary>
[TestClass]
public class WebSocketConnectionTests
{
    private TestWebApplicationFactory? webApplicationFactory;
    private HttpClient? httpClient;

    [TestInitialize]
    public void TestInitialize()
    {
        // Create in-memory test server with mocked RabbitMQ
        webApplicationFactory = new TestWebApplicationFactory();
        httpClient = webApplicationFactory.CreateClient();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        httpClient?.Dispose();
        webApplicationFactory?.Dispose();
    }

    [TestMethod]
    public async Task ConnectToWebSocket_WithValidToken_ShouldReceiveWelcomeMessage()
    {
        // Arrange
        var validToken = TestJwtTokenGenerator.GenerateValidToken(
            userId: "test-user-123",
            username: "TestUser");

        var webSocketClient = webApplicationFactory!.Server.CreateWebSocketClient();
        var receiveBuffer = new byte[1024 * 4];
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Build absolute WebSocket URI from server base address
        var baseUri = webApplicationFactory.Server.BaseAddress;
        var wsUri = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = "/ws",
            Query = $"token={Uri.EscapeDataString(validToken)}"
        }.Uri;

        // Act
        var webSocket = await webSocketClient.ConnectAsync(
            wsUri,
            cancellationTokenSource.Token);

        var result = await webSocket.ReceiveAsync(
            new ArraySegment<byte>(receiveBuffer),
            cancellationTokenSource.Token);

        var welcomeMessage = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);

        // Assert
        Assert.AreEqual(WebSocketState.Open, webSocket.State, "WebSocket should be in Open state");
        Assert.AreEqual(WebSocketMessageType.Text, result.MessageType, "Message should be text type");
        Assert.AreEqual("Welcome to WEBSOCKET!", welcomeMessage, "Should receive correct welcome message");
        Assert.IsFalse(result.EndOfMessage == false, "Message should be complete");

        // Cleanup
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test completed",
            CancellationToken.None);
        webSocket.Dispose();
    }

    [TestMethod]
    public async Task ConnectToWebSocket_WithInvalidToken_ShouldReturn401()
    {
        // Arrange
        var invalidToken = TestJwtTokenGenerator.GenerateInvalidToken();
        var webSocketClient = webApplicationFactory!.Server.CreateWebSocketClient();
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Build absolute WebSocket URI from server base address
        var baseUri = webApplicationFactory.Server.BaseAddress;
        var wsUri = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = "/ws",
            Query = $"token={Uri.EscapeDataString(invalidToken)}"
        }.Uri;

        // Act
        Exception? exception = null;
        WebSocket? webSocket = null;
        try
        {
            webSocket = await webSocketClient.ConnectAsync(
                wsUri,
                cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        // Assert
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            Assert.Fail("WebSocket should not connect with invalid token");
        }

        Assert.IsNotNull(exception, "Should throw exception for invalid token");
        Assert.IsTrue(
            exception.Message.Contains("401") || exception.Message.Contains("Unauthorized") || exception.Message.Contains("Invalid token"),
            $"Exception should indicate authentication failure, but got: {exception.Message}");
    }

    [TestMethod]
    public async Task ConnectToWebSocket_WithExpiredToken_ShouldReturn401()
    {
        // Arrange
        var expiredToken = TestJwtTokenGenerator.GenerateExpiredToken();
        var webSocketClient = webApplicationFactory!.Server.CreateWebSocketClient();
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Build absolute WebSocket URI from server base address
        var baseUri = webApplicationFactory.Server.BaseAddress;
        var wsUri = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = "/ws",
            Query = $"token={Uri.EscapeDataString(expiredToken)}"
        }.Uri;

        // Act
        Exception? exception = null;
        WebSocket? webSocket = null;
        try
        {
            webSocket = await webSocketClient.ConnectAsync(
                wsUri,
                cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        // Assert
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            Assert.Fail("WebSocket should not connect with expired token");
        }

        Assert.IsNotNull(exception, "Should throw exception for expired token");
        Assert.IsTrue(
            exception.Message.Contains("401") || exception.Message.Contains("Unauthorized") || exception.Message.Contains("Invalid token"),
            $"Exception should indicate authentication failure, but got: {exception.Message}");
    }

    [TestMethod]
    public async Task ConnectToWebSocket_WithoutToken_ShouldReturn401()
    {
        // Arrange
        var webSocketClient = webApplicationFactory!.Server.CreateWebSocketClient();
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Build absolute WebSocket URI from server base address (no token)
        var baseUri = webApplicationFactory.Server.BaseAddress;
        var wsUri = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = "/ws"
        }.Uri;

        // Act
        Exception? exception = null;
        WebSocket? webSocket = null;
        try
        {
            webSocket = await webSocketClient.ConnectAsync(
                wsUri,
                cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        // Assert
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            Assert.Fail("WebSocket should not connect without token");
        }

        Assert.IsNotNull(exception, "Should throw exception when token is missing");
        Assert.IsTrue(
            exception.Message.Contains("401") || exception.Message.Contains("Unauthorized") || exception.Message.Contains("Missing token"),
            $"Exception should indicate missing authentication, but got: {exception.Message}");
    }

    [TestMethod]
    public async Task HealthEndpoint_ShouldReturnHealthy()
    {
        // Arrange
        // (httpClient is already created in TestInitialize)

        // Act
        var response = await httpClient!.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Health endpoint should return 200 OK");
        Assert.AreEqual("healthy", content, "Health endpoint should return 'healthy'");
    }

    [TestMethod]
    public async Task VersionEndpoint_ShouldReturnVersion()
    {
        // Arrange
        // (httpClient is already created in TestInitialize)

        // Act
        var response = await httpClient!.GetAsync("/version");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Version endpoint should return 200 OK");
        Assert.IsFalse(string.IsNullOrEmpty(content), "Version should not be empty");
        Assert.IsTrue(content.Contains("."), "Version should be in semantic versioning format");
    }
}
