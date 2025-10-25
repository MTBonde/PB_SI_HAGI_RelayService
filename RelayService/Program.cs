using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RelayService;
using RelayService.Configuration;
using RelayService.Interfaces;
using RelayService.Models;
using RelayService.Services;

// Create webapp and websocket
var builder = WebApplication.CreateBuilder(args);

// Configuration
var jwtConfiguration = new JwtConfiguration
{
    // T0DO: Move to secure secret storage/ .env files
    Secret = "superSecretKey@345superSecretKey@345"
};
var webSocketConfiguration = new WebSocketConfiguration();
var rabbitMqConfiguration = new RabbitMqConfiguration
{
    HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq"
};

// Register configurations
builder.Services.AddSingleton(jwtConfiguration);
builder.Services.AddSingleton(webSocketConfiguration);
builder.Services.AddSingleton(rabbitMqConfiguration);

// Register services
builder.Services.AddSingleton<IJwtValidationService, JwtValidationService>();
builder.Services.AddSingleton<IWebSocketConnectionManager, WebSocketConnectionManager>();
builder.Services.AddSingleton<IMessageBroadcaster, MessageBroadcaster>();
builder.Services.AddSingleton<IRabbitMqConsumerService, RabbitMqConsumerService>();
builder.Services.AddSingleton<WebSocketEndpointHandler>();

var app = builder.Build();

// WebSocket middleware must be registered BEFORE endpoint routing
app.UseWebSockets();

// WebSocket endpoint handler - must come BEFORE MapGet endpoints
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws")
    {
        var webSocketHandler = context.RequestServices.GetRequiredService<WebSocketEndpointHandler>();
        await webSocketHandler.HandleWebSocketConnectionAsync(context);
    }
    else
    {
        await next(context);
    }
});

// Nice intro
Console.WriteLine($"RelayService v{ServiceVersion.Current} starting...");

// Start RabbitMQ consumer in background
var rabbitMqConsumerService = app.Services.GetRequiredService<IRabbitMqConsumerService>();
Task.Run(async () => await rabbitMqConsumerService.StartConsumerAsync());

// Declare endpoints (these create endpoint routing which runs after our middleware)
app.MapGet("/health", () => "healthy");
app.MapGet("/version", () => ServiceVersion.Current);

// broadcast listening
Console.WriteLine("RelayService listening on http://+:8080");

app.Run();

// Make Program class accessible to WebApplicationFactory for testing
public partial class Program { }
