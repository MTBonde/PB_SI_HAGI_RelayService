using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RelayService;
using RelayService.Configuration;
using RelayService.Interfaces;
using RelayService.Models;
using RelayService.Services;
using Hagi.Robust;

// Create webapp and websocket
var builder = WebApplication.CreateBuilder(args);

// add hagi.robus
builder.Services.AddHagiResilience();

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

// SessionService configuration
var sessionServiceUrl = Environment.GetEnvironmentVariable("SESSION_SERVICE_URL") ?? "http://sessionservice:8081";

// Register configurations
builder.Services.AddSingleton(jwtConfiguration);
builder.Services.AddSingleton(webSocketConfiguration);
builder.Services.AddSingleton(rabbitMqConfiguration);

// Register RabbitMQ connection manager (Single connection/channel for entire service)
builder.Services.AddSingleton<RabbitMqConnectionManager>();

// Register services
builder.Services.AddSingleton<IJwtValidationService, JwtValidationService>();
builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
builder.Services.AddSingleton<IWebSocketConnectionManager, WebSocketConnectionManager>();
builder.Services.AddSingleton<IMessageBroadcaster, MessageBroadcaster>();
builder.Services.AddSingleton<IRabbitMqConsumerService, RabbitMqConsumerService>();
builder.Services.AddSingleton<WebSocketEndpointHandler>();

// Register SessionService client with HTTP client
builder.Services.AddHttpClient<SessionServiceClient>();
builder.Services.AddSingleton<ISessionServiceClient>(serviceProvider =>
{
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var client = httpClientFactory.CreateClient();
    return new SessionServiceClient(client, sessionServiceUrl);
});

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "RelayService API",
        Version = ServiceVersion.Current,
        Description = "WebSocket relay service for broadcasting messages from RabbitMQ to connected clients"
    });
});

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

// Enable Swagger UI
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", $"RelayService API v{ServiceVersion.Current}");
    options.RoutePrefix = "swagger";
});

// broadcast listening
Console.WriteLine("RelayService listening on http://+:8080");

// Creates endpoint at /health/ready
app.MapReadinessEndpoint(); 

app.Run();

// Make Program class accessible to WebApplicationFactory for testing
public partial class Program { }
