using RabbitMQ.Client;
using RelayService.Configuration;

namespace RelayService.Services;

/// <summary>
/// Centralized manager for RabbitMQ connection and channel
/// Provides a single shared connection and channel for the entire service
/// </summary>
/// <remarks>
/// Best practice: One connection and one channel shared by all publishers and consumers
/// This reduces TCP overhead and follows RabbitMQ recommended patterns
/// </remarks>
public class RabbitMqConnectionManager : IDisposable
{
    private IConnection? connection;
    private IChannel? channel;
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private readonly SemaphoreSlim publishLock = new(1, 1);
    private readonly RabbitMqConfiguration configuration;
    private bool disposed = false;

    /// <summary>
    /// Initializes a new instance of the RabbitMqConnectionManager class
    /// </summary>
    /// <param name="configuration">RabbitMQ configuration settings</param>
    public RabbitMqConnectionManager(RabbitMqConfiguration configuration)
    {
        this.configuration = configuration;
    }

    /// <summary>
    /// Gets the shared channel, creating connection if needed
    /// </summary>
    /// <returns>The shared IChannel instance</returns>
    /// <remarks>Thread-safe - multiple callers will get the same channel instance</remarks>
    public async Task<IChannel> GetChannelAsync()
    {
        await EnsureConnectedAsync();
        return channel!;
    }

    /// <summary>
    /// Publishes a message to RabbitMQ with thread safety
    /// </summary>
    /// <param name="exchange">The exchange to publish to</param>
    /// <param name="routingKey">The routing key for message routing</param>
    /// <param name="body">The message body as byte array</param>
    /// <remarks>
    /// Thread-safe publishing using semaphore lock
    /// BasicPublish is not thread-safe, so we lock during publish
    /// </remarks>
    public async Task PublishAsync(string exchange, string routingKey, byte[] body)
    {
        await publishLock.WaitAsync();
        try
        {
            var channelInstance = await GetChannelAsync();
            await channelInstance.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                body: body);
        }
        finally
        {
            publishLock.Release();
        }
    }

    /// <summary>
    /// Ensures connection and channel are established
    /// </summary>
    private async Task EnsureConnectedAsync()
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
                    Console.WriteLine("RabbitMQ shared channel created");
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
                Console.WriteLine($"RabbitMQ connection established to {configuration.HostName}:{configuration.Port}");
                return;
            }
            catch (Exception exception)
            {
                retryCount++;
                Console.WriteLine($"RabbitMQ connection retry {retryCount}/{configuration.MaxRetries}... Error: {exception.Message}");

                if (retryCount < configuration.MaxRetries)
                {
                    await Task.Delay(configuration.RetryDelayMilliseconds);
                }
            }
        }

        throw new Exception($"Failed to connect to RabbitMQ after {configuration.MaxRetries} attempts");
    }

    /// <summary>
    /// Disposes the connection and channel
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;

        try
        {
            channel?.Dispose();
            connection?.Dispose();
            connectionLock.Dispose();
            publishLock.Dispose();

            Console.WriteLine("RabbitMQ connection manager disposed");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Error disposing RabbitMQ connection manager: {exception.Message}");
        }

        disposed = true;
    }
}
