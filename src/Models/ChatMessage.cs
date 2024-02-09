using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Newtonsoft.Json;

namespace Aire.Memory.Models
{
    public class ChatMessage
    {
        [JsonProperty("role", Required = Required.Always)]
        [OpenApiProperty(Description = "The role of the message's author")]
        public ChatRole? Role { get; set; }

        [JsonProperty("timestamp")]
        [OpenApiProperty(Description = "The message timestamp in Unix time")]
        public long Timestamp { get; set; }

        [JsonProperty("content")]
        [OpenApiProperty(Description = "Message content")]
        public string? Content { get; set; }

        [JsonProperty("rating")]
        [OpenApiProperty(Description = "Message rating")]
        public int? Rating { get; set; }

        [JsonProperty("answer")]
        [OpenApiProperty(Description = "Answer content")]
        public string? Answer { get; set; }
    }
}
