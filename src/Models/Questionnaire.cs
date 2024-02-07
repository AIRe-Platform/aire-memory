using Newtonsoft.Json;
using Aire.Sdk.Helpers;

namespace Aire.Memory.Models
{
    public class Questionnaire
    {
        [JsonProperty("id", Required = Required.Always)]
        public Guid? Id { get; set; } = Guid.NewGuid();

        [JsonProperty("name", Required = Required.Always)]
        public string? Name { get; set; }

        [JsonProperty("lang", Required = Required.Always)]
        public string? Lang { get; set; }

        [JsonProperty("modified")]
        public DateTime Modified { get; set; }

        [JsonProperty("keywords", Required = Required.Always)]
        public string[]? Keywords { get; set; }

        [JsonProperty("preliminary")]
        public Preliminary? Preliminary { get; set; }

        [JsonProperty("content", Required = Required.Always)]
        public List<QuestionnaireContent>? Content { get; set; }

        public Questionnaire() { }

        public Questionnaire(QuestionnaireEntity questionnaireEntity)
        {
            Id = questionnaireEntity.Id;
            Name = questionnaireEntity.Name;
            Lang = questionnaireEntity.Lang;
            Modified = questionnaireEntity.Modified;
            Keywords = questionnaireEntity.Keywords;
            Preliminary = questionnaireEntity.Preliminary?.JsonToObject<Preliminary>();
            Content = questionnaireEntity.Content?.JsonToObject<List<QuestionnaireContent>>();
        }
    }

    public class Preliminary
    {
        [JsonProperty("properties")]
        public Dictionary<string, dynamic>? Properties { get; set; }

        [JsonProperty("required")]
        public List<string>? Required { get; set; }
    }
}
