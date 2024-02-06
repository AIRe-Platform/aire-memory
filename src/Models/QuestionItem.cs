using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aire.Memory.Models
{
    [NotMapped]
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
        [NotMapped]
        public QuestionOptions? Options { get; set; }
    }
}
