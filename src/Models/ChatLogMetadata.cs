using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Newtonsoft.Json;

namespace Aire.Memory.Models
{
    public class ChatLogMetadata
    {
        [JsonProperty("id", Required = Required.Always)]
        [OpenApiProperty(Description = "The chat log identifier")]
        public Guid Id { get; set; }

        [JsonProperty("time", Required = Required.Always)]
        [OpenApiProperty(Description = "The timestamp when the chat log was last modified")]
        public DateTime Time { get; set; }

        public ChatLogMetadata(Guid id, DateTime time)
        {
            Id = id;
            Time = time;
        }
    }
}
