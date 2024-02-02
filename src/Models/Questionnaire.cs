using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using System.Data.Common;
using Aire.Sdk.Helpers;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json.Linq;

namespace Aire.Memory.Models
{
    public class Questionnaire
    {
        [JsonProperty("id")]
        public Guid? Id { get; set; } = Guid.NewGuid();
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("lang")]
        public string? Lang { get; set; }
        [JsonProperty("modified")]
        public DateTime Modified { get; set; }

        [JsonProperty("keywords")]
        public string[]? Keywords { get; set; }

        [JsonProperty("preliminary")]
        public Preliminary? Preliminary { get; set; }

        [JsonProperty("content")]
        public List<QuestionnaireContent>? Content { get; set; }

        public Questionnaire() {

        }

        public Questionnaire(QuestionnaireEntity questionnaireEntity) {
            Id = questionnaireEntity.Id;
            Name = questionnaireEntity.Name;
            Lang = questionnaireEntity.Lang;
            Modified = questionnaireEntity.Modified;
            Keywords = questionnaireEntity.Keywords;
            Preliminary = questionnaireEntity.Preliminary.JsonToObject<Preliminary>();
            Content = questionnaireEntity.Content.JsonToObject<List<QuestionnaireContent>>();
        }
    }

    [NotMapped]
    public class Preliminary
    {
        [JsonProperty("properties")]
        public Dictionary<string, Property>? Properties { get; set; }
        [JsonProperty("required")]
        public List<string>? Required { get; set; }
    }

    [NotMapped]
    public class Property
    {
        [JsonProperty("type")]
        public string? Type { get; set; }
    }
}
