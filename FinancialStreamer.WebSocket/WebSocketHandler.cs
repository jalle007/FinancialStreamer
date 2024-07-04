using FinancialStreamer.Core.Interfaces;
using FinancialStreamer.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FinancialStreamer.WebSocket
{
    public class WebSocketHandler
    {
        private static readonly Dictionary<string, List<System.Net.WebSockets.WebSocket>> _subscribers = new Dictionary<string, List<System.Net.WebSockets.WebSocket>>();
        private static readonly SemaphoreSlim _subscribersLock = new SemaphoreSlim(1, 1);
        private readonly IPriceDataProvider _priceDataProvider;
        private readonly ILogger<WebSocketHandler> _logger;

        public WebSocketHandler(IPriceDataProvider priceDataProvider, ILogger<WebSocketHandler> logger)
        {
            _priceDataProvider = priceDataProvider;
            _logger = logger;
        }

        public async Task HandleWebSocketAsync(System.Net.WebSockets.WebSocket webSocket)
        {
            try
            {
                _logger.LogInformation("WebSocket connection established");
                while (webSocket.State == WebSocketState.Open)
                {
                    var buffer = new byte[1024 * 4];
                    _logger.LogInformation("Waiting for WebSocket message...");
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    _logger.LogInformation($"Received WebSocket message. Type: {result.MessageType}, Count: {result.Count}");

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by the WebSocket client", CancellationToken.None);
                        _logger.LogInformation("WebSocket connection closed by client");
                        return;
                    }

                    var messageJson = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger.LogInformation($"Received raw message: {messageJson}");

                    try
                    {
                        var message = JsonSerializer.Deserialize<WebSocketMessage>(messageJson);
                        _logger.LogInformation($"Deserialized message: Method={message?.Method}, Params={string.Join(",", message?.Params ?? new List<string>())}");

                        if (message?.Method.Equals("SUBSCRIBE", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            foreach (var symbol in message.Params ?? new List<string>())
                            {
                                _logger.LogInformation($"Subscribing to {symbol}");
                                await AddSubscriberAsync(symbol, webSocket);
                            }
                        }
                        else if (message?.Method.Equals("UNSUBSCRIBE", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            foreach (var symbol in message.Params ?? new List<string>())
                            {
                                _logger.LogInformation($"Unsubscribing from {symbol}");
                                await RemoveSubscriberAsync(symbol, webSocket);
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Unrecognized method: {message?.Method}");
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Error deserializing WebSocket message");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket error");
            }
            finally
            {
                _logger.LogInformation("WebSocket connection handling completed");
            }
        }

        private async Task AddSubscriberAsync(string symbol, System.Net.WebSockets.WebSocket webSocket)
        {
            await _subscribersLock.WaitAsync();
            try
            {
                if (!_subscribers.ContainsKey(symbol))
                {
                    _subscribers[symbol] = new List<System.Net.WebSockets.WebSocket>();
                    _logger.LogInformation($"Creating new subscriber list for {symbol}");
                    await _priceDataProvider.SubscribeToPriceUpdatesAsync(symbol, async priceUpdate =>
                    {
                        await BroadcastPriceUpdateAsync(symbol, priceUpdate);
                    });
                }

                _subscribers[symbol].Add(webSocket);
                _logger.LogInformation($"Added subscriber for {symbol}");
            }
            finally
            {
                _subscribersLock.Release();
            }
        }

        private async Task RemoveSubscriberAsync(string symbol, System.Net.WebSockets.WebSocket webSocket)
        {
            await _subscribersLock.WaitAsync();
            try
            {
                if (_subscribers.TryGetValue(symbol, out var subscribers))
                {
                    subscribers.Remove(webSocket);
                    _logger.LogInformation($"Removed subscriber for {symbol}");
                    if (subscribers.Count == 0)
                    {
                        _subscribers.Remove(symbol);
                        _logger.LogInformation($"No more subscribers for {symbol}. Unsubscribing from price updates.");
                        // Optionally, you could stop receiving updates from the data provider for this symbol
                    }
                }
            }
            finally
            {
                _subscribersLock.Release();
            }
        }

        private async Task BroadcastPriceUpdateAsync(string symbol, PriceUpdate priceUpdate)
        {
            await _subscribersLock.WaitAsync();
            try
            {
                if (_subscribers.TryGetValue(symbol, out var subscribers))
                {
                    var message = JsonSerializer.Serialize(priceUpdate);
                    var messageBytes = new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(message));
                    _logger.LogInformation($"Broadcasting price update for {symbol}: {message}");

                    foreach (var subscriber in subscribers.ToList())
                    {
                        if (subscriber.State == WebSocketState.Open)
                        {
                            await subscriber.SendAsync(messageBytes, WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        else
                        {
                            subscribers.Remove(subscriber);
                            _logger.LogInformation($"Removed closed WebSocket for {symbol}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error broadcasting price update for {symbol}");
            }
            finally
            {
                _subscribersLock.Release();
            }
        }
    }
}
