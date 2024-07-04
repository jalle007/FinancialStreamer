using FinancialStreamer.Core.Interfaces;
using FinancialStreamer.Infrastructure.Configurations;
using FinancialStreamer.Infrastructure.Services;
using Microsoft.AspNetCore.WebSockets;
using FinancialStreamer.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
builder.Services.AddWebSockets(options => { });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Configure WebSocket endpoint
app.UseWebSockets();
app.MapGet("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var webSocketHandler = app.Services.GetRequiredService<WebSocketHandler>();
        await webSocketHandler.HandleWebSocketAsync(webSocket);
    }
    else
    {
        context.Response.StatusCode = 400;
    }
});

app.MapControllers();

app.Run();

 
