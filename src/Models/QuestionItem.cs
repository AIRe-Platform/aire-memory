using Newtonsoft.Json;

namespace Aire.Memory.Models
{
    public class QuestionItem
    {
        [JsonProperty("id", Required = Newtonsoft.Json.Required.Always)]
        public string? Id { get; set; }

        [JsonProperty("question", Required = Newtonsoft.Json.Required.Always)]
        public string? Question { get; set; }

        [JsonProperty("keywords")]
        public string[]? Keywords { get; set; }

        [JsonProperty("prompt")]
        public string? Prompt { get; set; }

        [JsonProperty("type", Required = Newtonsoft.Json.Required.Always)]
        public QuestionOptionType? Type { get; set; }

        [JsonProperty("options")]
        public object? Options { get; set; }

        [JsonProperty("required")]
        public bool? Required { get; set; } = false;
    }
}
