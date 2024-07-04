﻿using FinancialStreamer.Core.Interfaces;
using FinancialStreamer.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
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
        private static readonly ConcurrentDictionary<string, List<System.Net.WebSockets.WebSocket>> _subscribers = new ConcurrentDictionary<string, List<System.Net.WebSockets.WebSocket>>();
        private readonly IPriceDataProvider _priceDataProvider;
        private readonly ILogger<WebSocketHandler> _logger;

        public WebSocketHandler(IPriceDataProvider priceDataProvider, ILogger<WebSocketHandler> logger)
        {
            _priceDataProvider = priceDataProvider;
            _logger = logger;
        }

        /// <summary>
        /// Handles WebSocket connections, receiving messages, and processing subscription/unsubscription requests.
        /// </summary>
        /// <param name="webSocket">The WebSocket connection.</param>
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
                        if (message != null)
                        {
                            _logger.LogInformation($"Deserialized message: Method={message.Method}, Params={string.Join(",", message.Params ?? new List<string>())}");
                            await HandleMessageAsync(message, webSocket);
                        }
                        else
                        {
                            _logger.LogWarning("Message deserialization resulted in null");
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

        /// <summary>
        /// Handles incoming WebSocket messages and processes subscription and unsubscription requests.
        /// </summary>
        /// <param name="message">The deserialized WebSocket message.</param>
        /// <param name="webSocket">The WebSocket connection.</param>
        private async Task HandleMessageAsync(WebSocketMessage message, System.Net.WebSockets.WebSocket webSocket)
        {
            _logger.LogInformation($"Handling message: Method={message.Method}");
            if (message.Method.Equals("SUBSCRIBE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Handling SUBSCRIBE message for params: {string.Join(", ", message.Params)}");
                foreach (var symbol in message.Params ?? new List<string>())
                {
                    _logger.LogInformation($"Subscribing to {symbol}");
                    await AddSubscriberAsync(symbol, webSocket);
                }
            }
            else if (message.Method.Equals("UNSUBSCRIBE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Handling UNSUBSCRIBE message for params: {string.Join(", ", message.Params)}");
                foreach (var symbol in message.Params ?? new List<string>())
                {
                    _logger.LogInformation($"Unsubscribing from {symbol}");
                    await RemoveSubscriberAsync(symbol, webSocket);
                }
            }
            else
            {
                _logger.LogWarning($"Unrecognized method: {message.Method}");
            }
        }

        /// <summary>
        /// Adds a subscriber for a specific financial instrument symbol.
        /// </summary>
        /// <param name="symbol">The financial instrument symbol to subscribe to.</param>
        /// <param name="webSocket">The WebSocket connection.</param>
        private async Task AddSubscriberAsync(string symbol, System.Net.WebSockets.WebSocket webSocket)
        {
            _logger.LogInformation($"Attempting to add subscriber for {symbol}");

            // Optimization for handling many subscribers
            // Using ConcurrentDictionary to handle concurrent access to subscribers list
            _subscribers.AddOrUpdate(symbol,
                new List<System.Net.WebSockets.WebSocket> { webSocket },
                (key, existingList) =>
                {
                    lock (existingList)
                    {
                        existingList.Add(webSocket);
                    }
                    return existingList;
                });

            _logger.LogInformation($"Added subscriber for {symbol}");

            if (_subscribers[symbol].Count == 1)
            {
                // If this is the first subscriber for the symbol, subscribe to the data provider
                _logger.LogInformation($"Creating new subscriber list for {symbol}");
                _ = _priceDataProvider.SubscribeToPriceUpdatesAsync(symbol, async priceUpdate =>
                {
                    await BroadcastPriceUpdateAsync(symbol, priceUpdate);
                });
            }
        }

        /// <summary>
        /// Removes a subscriber for a specific financial instrument symbol.
        /// </summary>
        /// <param name="symbol">The financial instrument symbol to unsubscribe from.</param>
        /// <param name="webSocket">The WebSocket connection.</param>
        private async Task RemoveSubscriberAsync(string symbol, System.Net.WebSockets.WebSocket webSocket)
        {
            _logger.LogInformation($"Attempting to remove subscriber for {symbol}");

            if (_subscribers.TryGetValue(symbol, out var subscribers))
            {
                lock (subscribers)
                {
                    subscribers.Remove(webSocket);
                }

                _logger.LogInformation($"Removed subscriber for {symbol}");

                if (subscribers.Count == 0)
                {
                    _subscribers.TryRemove(symbol, out _);
                    _logger.LogInformation($"No more subscribers for {symbol}. Unsubscribing from price updates.");
                    await _priceDataProvider.UnsubscribeFromPriceUpdatesAsync(symbol);
                }
            }
            else
            {
                _logger.LogWarning($"No subscribers found for {symbol}");
            }
        }

        /// <summary>
        /// Broadcasts a price update to all subscribers of a specific financial instrument symbol.
        /// </summary>
        /// <param name="symbol">The financial instrument symbol.</param>
        /// <param name="priceUpdate">The price update to broadcast.</param>
        private async Task BroadcastPriceUpdateAsync(string symbol, PriceUpdate priceUpdate)
        {
            _logger.LogInformation($"Attempting to broadcast price update for {symbol}");

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
                        lock (subscribers)
                        {
                            subscribers.Remove(subscriber);
                        }
                        _logger.LogInformation($"Removed closed WebSocket for {symbol}");
                    }
                }
            }
        }

    }


}
