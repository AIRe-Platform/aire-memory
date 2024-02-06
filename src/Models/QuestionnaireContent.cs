using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aire.Memory.Models
{
    [NotMapped]
    public class QuestionnaireContent
    {
        [JsonProperty("id", Required = Required.Always)]
        public string? Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("keywords")]
        public string[]? Keywords { get; set; }

        [NotMapped]
        public List<QuestionItem>? Questions { get; set; }
    }
}