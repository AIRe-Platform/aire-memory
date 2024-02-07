using Newtonsoft.Json;

namespace Aire.Memory.Models
{
    public class QuestionItem
    {
        [JsonProperty("id", Required = Required.Always)]
        public string? Id { get; set; }

        [JsonProperty("question")]
        public string? Question { get; set; }

        [JsonProperty("keywords")]
        public string[]? Keywords { get; set; }

        [JsonProperty("prompt")]
        public string? Prompt { get; set; }

        [JsonProperty("options")]
        public QuestionOptions? Options { get; set; }
    }
}
