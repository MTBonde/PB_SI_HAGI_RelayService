using RabbitMQ.Client;

Console.WriteLine($"RelayService v{RelayService.ServiceVersion.Current} starting...");

var rabbitMqHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";

Console.WriteLine($"Connecting to RabbitMQ at {rabbitMqHost}...");

try
{
    var factory = new ConnectionFactory
    {
        HostName = rabbitMqHost,
        Port = 5672,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(30),
        AutomaticRecoveryEnabled = true
    };

    using var connection = factory.CreateConnection();
    using var channel = connection.CreateModel();

    Console.WriteLine("RelayService connected to RabbitMQ");

    // Declare relay exchanges
    channel.ExchangeDeclare(
        exchange: "relay.chat.global",
        type: ExchangeType.Fanout,
        durable: true,
        autoDelete: false);
    Console.WriteLine("Declared exchange: relay.chat.global (fanout, durable)");

    channel.ExchangeDeclare(
        exchange: "relay.game.events",
        type: ExchangeType.Topic,
        durable: true,
        autoDelete: false);
    Console.WriteLine("Declared exchange: relay.game.events (topic, durable)");

    channel.ExchangeDeclare(
        exchange: "relay.session.events",
        type: ExchangeType.Topic,
        durable: true,
        autoDelete: false);
    Console.WriteLine("Declared exchange: relay.session.events (topic, durable)");

    Console.WriteLine("RelayService is running. Press Ctrl+C to exit.");

    // Keep the worker running
    var cancellationTokenSource = new CancellationTokenSource();
    Console.CancelKeyPress += (sender, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellationTokenSource.Cancel();
    };

    cancellationTokenSource.Token.WaitHandle.WaitOne();
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Environment.Exit(1);
}

Console.WriteLine("RelayService stopped.");
