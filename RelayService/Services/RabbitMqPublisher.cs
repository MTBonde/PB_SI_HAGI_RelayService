using System.Text;
using RabbitMQ.Client;
using RelayService.Configuration;
using RelayService.Interfaces;

namespace RelayService.Services;

/// <summary>
/// Service for publishing messages to RabbitMQ exchanges
/// Handles connection management and message publishing
/// </summary>
public class RabbitMqPublisher : IRabbitMqPublisher
{
    private readonly RabbitMqConfiguration configuration;
    private IConnection? connection;
    private IChannel? channel;
    private readonly SemaphoreSlim connectionLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the RabbitMqPublisher class with the specified configuration
    /// </summary>
    /// <param name="configuration">RabbitMQ configuration containing connection settings</param>
    public RabbitMqPublisher(RabbitMqConfiguration configuration)
    {
        this.configuration = configuration;
    }

    /// <summary>
    /// Publishes a message to the specified RabbitMQ exchange
    /// </summary>
    /// <param name="exchange">The name of the exchange to publish to</param>
    /// <param name="routingKey">The routing key for the message (use empty string for fanout exchanges)</param>
    /// <param name="message">The message content as a string</param>
    public async Task PublishAsync(string exchange, string routingKey, string message)
    {
        try
        {
            // Ensure connection and channel are established
            await EnsureConnectionAsync();

            if (channel == null)
            {
                Console.WriteLine("Failed to publish message: channel not initialized");
                return;
            }

            // Convert message to byte array
            var body = Encoding.UTF8.GetBytes(message);

            // Publish message to the specified exchange
            await channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                body: body);

            Console.WriteLine($"Published message to exchange '{exchange}' with routing key '{routingKey}'");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error publishing message to RabbitMQ: {exception.Message}");
        }
    }

    /// <summary>
    /// Ensures that the RabbitMQ connection and channel are established
    /// Creates new connection and channel if they don't exist or are closed
    /// </summary>
    private async Task EnsureConnectionAsync()
    {
        await connectionLock.WaitAsync();
        try
        {
            // Check if connection needs to be established or re-established
            if (connection == null || !connection.IsOpen)
            {
                await ConnectAsync();
            }

            // Check if channel needs to be created or re-created
            if (channel == null || channel.IsClosed)
            {
                if (connection != null && connection.IsOpen)
                {
                    channel = await connection.CreateChannelAsync();
                    Console.WriteLine("RabbitMQ publisher channel created");
                }
            }
        }
        finally
        {
            connectionLock.Release();
        }
    }

    /// <summary>
    /// Establishes connection to RabbitMQ with retry logic
    /// </summary>
    private async Task ConnectAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration.HostName,
            Port = configuration.Port
        };

        int retryCount = 0;
        while (retryCount < configuration.MaxRetries)
        {
            try
            {
                connection = await factory.CreateConnectionAsync();
                Console.WriteLine("RabbitMQ publisher connected");
                return;
            }
            catch (Exception exception)
            {
                retryCount++;
                Console.WriteLine($"RabbitMQ publisher retry {retryCount}/{configuration.MaxRetries}... Error: {exception.Message}");
                await Task.Delay(configuration.RetryDelayMilliseconds);
            }
        }

        Console.WriteLine("RabbitMQ publisher failed to connect after maximum retries");
    }
}
