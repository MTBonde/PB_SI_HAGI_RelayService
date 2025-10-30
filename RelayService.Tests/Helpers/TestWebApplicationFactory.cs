using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RelayService.Interfaces;
using RelayService.Models;

namespace RelayService.Tests.Helpers;

/// <summary>
/// Custom WebApplicationFactory for testing that replaces RabbitMQ with a mock implementation
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public MockRabbitMqPublisher? MockPublisher { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real RabbitMQ consumer service
            var rabbitMqConsumerDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IRabbitMqConsumerService));

            if (rabbitMqConsumerDescriptor != null)
            {
                services.Remove(rabbitMqConsumerDescriptor);
            }

            // Remove the real RabbitMQ publisher service
            var rabbitMqPublisherDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IRabbitMqPublisher));

            if (rabbitMqPublisherDescriptor != null)
            {
                services.Remove(rabbitMqPublisherDescriptor);
            }

            // Register mocks
            services.AddSingleton<IRabbitMqConsumerService, MockRabbitMqConsumerService>();

            MockPublisher = new MockRabbitMqPublisher();
            services.AddSingleton<IRabbitMqPublisher>(MockPublisher);
        });
    }

    /// <summary>
    /// Mock implementation of IRabbitMqConsumerService that does nothing
    /// Used in tests where RabbitMQ is not available
    /// </summary>
    private class MockRabbitMqConsumerService : IRabbitMqConsumerService
    {
        public Task StartConsumerAsync()
        {
            // Do nothing - no RabbitMQ connection needed in tests
            return Task.CompletedTask;
        }

        public Task AddPrivateQueueForUserAsync(string username)
        {
            // Do nothing - no RabbitMQ connection needed in tests
            return Task.CompletedTask;
        }

        public Task RemovePrivateQueueForUserAsync(string username)
        {
            // Do nothing - no RabbitMQ connection needed in tests
            return Task.CompletedTask;
        }

        public Task AddServerQueueAsync(string serverId)
        {
            // Do nothing - no RabbitMQ connection needed in tests
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Mock implementation of IRabbitMqPublisher that tracks published messages for testing
/// </summary>
public class MockRabbitMqPublisher : IRabbitMqPublisher
{
    private readonly List<PublishedMessage> publishedMessages = new();

    public Task PublishAsync(string exchange, string routingKey, string message)
    {
        publishedMessages.Add(new PublishedMessage
        {
            Exchange = exchange,
            RoutingKey = routingKey,
            Message = message,
            Timestamp = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    public Task PublishChatMessageAsync(string exchange, string routingKey, ChatMessage chatMessage)
    {
        var json = JsonSerializer.Serialize(chatMessage);
        publishedMessages.Add(new PublishedMessage
        {
            Exchange = exchange,
            RoutingKey = routingKey,
            Message = json,
            Timestamp = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    public List<PublishedMessage> GetPublishedMessages() => publishedMessages;

    public void ClearPublishedMessages() => publishedMessages.Clear();
}

/// <summary>
/// Represents a message that was published to RabbitMQ during testing
/// </summary>
public class PublishedMessage
{
    public string Exchange { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
