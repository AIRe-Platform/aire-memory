using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Newtonsoft.Json;
using Aire.Sdk.Helpers;
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

        // public QuestionnaireContent(string id, string name, string keywords, string questions) {
        //     Id = id;
        //     Name = name;
        //     Keywords = keywords;
        //     Questions = questions?.JsonToObject<List<QuestionItem>>();
        // }
    }
}