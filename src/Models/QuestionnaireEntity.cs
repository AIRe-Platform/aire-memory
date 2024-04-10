using Aire.Sdk.Azure;
using Aire.Sdk.Helpers;
using Aire.Sdk.Models.Resources;

namespace Aire.Memory.Models;

/// <summary>
/// ID: PartitionKey, Rowkey
/// </summary>
[EntityTable("Questionnaires")]
public class QuestionnaireEntity : BaseTableEntity
{
    public string? Name { get; set; }
    public string? Lang { get; set; }
    public string? Keywords { get; set; }
    public string? Content { get; set; }
    public string? EmbeddingId { get; set; }

    public QuestionnaireEntity()
    {
        string id = Guid.NewGuid().ToString();
        PartitionKey ??= id;
        RowKey ??= id;
    }

    public string Id()
    {
        return RowKey ?? "";
    }

    public QuestionnaireEntity(Questionnaire questionnaire)
    {
        PartitionKey = questionnaire.Id.ToString();
        RowKey = questionnaire.Id.ToString();
        Name = questionnaire.Name;
        Lang = questionnaire.Lang;
        Keywords = string.Join(",", questionnaire.Keywords!);
        Content = questionnaire.Content?.ObjectToJson();
    }

    public Questionnaire ToModel()
    {
        var model = new Questionnaire
        {
            Name = Name,
            Lang = Lang,
            Modified = Timestamp.HasValue ? Timestamp.Value.UtcDateTime : DateTime.UtcNow,
            Keywords = Keywords?.Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            Content = Content?.JsonToObject<List<QuestionnaireContent>>()
        };

        if (Guid.TryParse(RowKey, out var id))
            model.Id = id;

        return model;
    }
}
