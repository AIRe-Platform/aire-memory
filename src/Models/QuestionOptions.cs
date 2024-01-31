using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Newtonsoft.Json;
using Aire.Sdk.Helpers;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aire.Memory.Models
{
    [NotMapped]
    public class QuestionOptions
    {
        [JsonProperty("type", Required = Required.Always)]
        public string? Type { get; set; }

        [JsonProperty("min")]
        public int? Min { get; set; }

        [JsonProperty("max")]
        public int? Max { get; set; }

        [JsonProperty("values")]
        public string[]? Values { get; set; }

        [JsonProperty("multiselect")]
        public bool Multiselect { get; set; }

        [JsonProperty("max_len")]
        public int? MaxLen { get; set; }

        [JsonProperty("match")]
        public string? Match { get; set; }

        [JsonProperty("default")]
        public int? Default { get; set; }

        public QuestionOptions? GetQuestionOptions(string json) {
            return json?.JsonToObject<QuestionOptions>();
        }
    }
}
