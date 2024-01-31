using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using System.Data.Common;
using Aire.Sdk.Helpers;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aire.Memory.Models
{
    public class QuestionnaireEntity
    {
        public Guid? Id { get; set; } = Guid.NewGuid();
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("lang")]
        public string? Lang { get; set; }
        public DateTime Modified { get; set; }

        [JsonProperty("keywords")]
        public string? Keywords { get; set; }

        [JsonProperty("preliminary")]
        public string? Preliminary { get; set; }

        [JsonProperty("content")]
        [NotMapped]
        public QuestionnaireContent? Content { get; set; }

        // public QuestionnaireEntity(Guid id, string name, string lang, DateTime modified, string keywords, string preliminary, string content) {
        //     Id = id;
        //     Name = name;
        //     Lang = lang;
        //     Modified = modified;
        //     Keywords = keywords;
        //     Preliminary = preliminary;
        //     Content = content?.JsonToObject<QuestionnaireContent>();
        // }
    }
}
