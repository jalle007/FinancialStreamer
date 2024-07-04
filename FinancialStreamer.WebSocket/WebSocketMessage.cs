using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FinancialStreamer.WebSocket
{
    //public enum WebSocketMethod
    //{
    //    SUBSCRIBE,
    //    UNSUBSCRIBE
    //}

    public class WebSocketMessage
    {
        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("params")]
        public List<string>? Params { get; set; }
    }
}
