using Aire.Sdk.Helpers;

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

        public QuestionnaireEntity() {
        
        }

        public QuestionnaireEntity(Questionnaire questionnaire) {
            Id = questionnaire.Id;
            Name = questionnaire.Name;
            Lang = questionnaire.Lang;
            Modified = questionnaire.Modified;
            Keywords = questionnaire.Keywords;
            
            if(questionnaire.Preliminary is not null) {
                Preliminary = questionnaire.Preliminary.ObjectToJson<Preliminary>();
            }
            if(questionnaire.Content is not null) {
                Content = questionnaire.Content.ObjectToJson<List<QuestionnaireContent>>();
            }
            
        }
    }

}