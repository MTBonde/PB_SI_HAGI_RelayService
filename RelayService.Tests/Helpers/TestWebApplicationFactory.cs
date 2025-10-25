using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RelayService.Interfaces;

namespace RelayService.Tests.Helpers;

/// <summary>
/// Custom WebApplicationFactory for testing that replaces RabbitMQ with a mock implementation
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real RabbitMQ consumer service
            var rabbitMqServiceDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IRabbitMqConsumerService));

            if (rabbitMqServiceDescriptor != null)
            {
                services.Remove(rabbitMqServiceDescriptor);
            }

            // Register a mock RabbitMQ consumer that does nothing
            services.AddSingleton<IRabbitMqConsumerService, MockRabbitMqConsumerService>();
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
    }
}
