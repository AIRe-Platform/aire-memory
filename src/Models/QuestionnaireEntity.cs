using Aire.Sdk.Helpers;
using Aire.Sdk.Models.Resources;

namespace Aire.Memory.Models
{
    public class QuestionnaireEntity
    {
        public Guid? Id { get; set; } = Guid.NewGuid();
        public string? Name { get; set; }
        public string? Lang { get; set; }
        public DateTime Modified { get; set; }
        public string[]? Keywords { get; set; }
        public string? Preliminary { get; set; }
        public string? Content { get; set; }

        public QuestionnaireEntity() { }

        public QuestionnaireEntity(Questionnaire questionnaire)
        {
            Id = questionnaire.Id;
            Name = questionnaire.Name;
            Lang = questionnaire.Lang;
            Modified = questionnaire.Modified;
            Keywords = questionnaire.Keywords;
            Preliminary = questionnaire.Preliminary?.ObjectToJson();
            Content = questionnaire.Content?.ObjectToJson();
        }

        public Questionnaire ToModel()
        {
            return new Questionnaire {
                Id = Id,
                Name = Name,
                Lang = Lang,
                Modified = Modified,
                Keywords = Keywords,
                Preliminary = Preliminary?.JsonToObject<Preliminary>(),
                Content = Content?.JsonToObject<List<QuestionnaireContent>>()
            };
        }
    }
}
