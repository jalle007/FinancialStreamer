using FinancialStreamer.Core.Interfaces;
using FinancialStreamer.Infrastructure.Configurations;
using FinancialStreamer.Infrastructure.Services;
using FinancialStreamer.WebSocket;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

// Add configuration
builder.Services.Configure<TiingoSettings>(builder.Configuration.GetSection("Tiingo"));
builder.Services.AddSingleton<IPriceDataProvider, TiingoPriceDataProvider>();
builder.Services.AddSingleton<WebSocketHandler>();

// Add WebSocket services
builder.Services.AddWebSockets(options =>
{
    options.AllowedOrigins.Add("*");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // Enable serving static files

// Redirect to test_page.html on startup
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/")
    {
        context.Response.Redirect("/test_page.html");
        return;
    }
    await next();
});

// Configure WebSocket endpoint
app.UseWebSockets();

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger<Program>();

app.MapGet("/ws", async context =>
{
    logger.LogInformation("WebSocket connection attempt received");
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var webSocketHandler = app.Services.GetRequiredService<WebSocketHandler>();
        await webSocketHandler.HandleWebSocketAsync(webSocket);
    }
    else
    {
        logger.LogWarning("Invalid WebSocket request received");
        context.Response.StatusCode = 400;
    }
});

app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var testPageUrl = "https://localhost:7057/test_page.html";
    var swaggerUrl = "https://localhost:7057/swagger/index.html";
    logger.LogInformation($"Test Page: {testPageUrl}");
    logger.LogInformation($"Swagger: {swaggerUrl}");
});

app.Run();
