using Newtonsoft.Json;

namespace Aire.Memory.Models
{
    public class QuestionnaireContent
    {
        [JsonProperty("id", Required = Required.Always)]
        public string? Id { get; set; }

        [JsonProperty("name", Required = Required.Always)]
        public string? Name { get; set; }

        [JsonProperty("keywords")]
        public string[]? Keywords { get; set; }

        [JsonProperty("questions", Required = Required.Always)]
        public List<QuestionItem>? Questions { get; set; }
    }
}