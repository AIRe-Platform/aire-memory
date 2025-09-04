// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using System.Runtime.Serialization;
using Aire.Sdk.Azure;
using Aire.Sdk.Helpers;
using Aire.Sdk.Models.Resources;

namespace Aire.Memory.Models;

/// <summary>
/// PartitionKey = First 2 letters (minimum length for a keyword)
/// RowKey = Keyword value
/// </summary>
[EntityTable("Keywords")]
public class KeywordValueEntity : BaseTableEntity
{
    public int ContentCount { get; set; }
    public int QuestionnaireCount { get; set; }
    public string? Translations { get; set; }
    public string? Prompt { get; set; }
    public string? Document { get; set; }

    [IgnoreDataMember]
    public Dictionary<string, dynamic>? Stats
    {
        get => new()
        {
            { ResourceTypes.Content, ContentCount },
            { ResourceTypes.Questionnaire, QuestionnaireCount },
        };

        set
        {
            ContentCount = value?.GetValueOrDefault(ResourceTypes.Content) ?? 0;
            QuestionnaireCount = value?.GetValueOrDefault(ResourceTypes.Questionnaire) ?? 0;
        }
    }

    public string Id()
    {
        return RowKey ?? "";
    }

    public static string? PartitionFromValue(string value)
    {
        if (value.Length < 2)
        {
            return null;
        }

        return value[..2];
    }

    public KeywordValueEntity() { }
    public KeywordValueEntity(string keyword)
    {
        PartitionKey = PartitionFromValue(keyword);
        RowKey = keyword;
        Stats = [];
        Translations = null;
        Prompt = null;
        Document = null;
    }

    public KeywordValueEntity(Keyword keyword)
    {
        PartitionKey = PartitionFromValue(keyword.Value!);
        RowKey = keyword.Value;
        Stats = [];
        Translations = keyword.Translations.ObjectToJson();
        Prompt = keyword.Prompt;
        Document = keyword.Document;
    }

    public Keyword ToModel()
    {
        return new Keyword()
        {
            Value = Id(),
            Stats = Stats,
            Translations = Translations?.JsonToObject<List<Translation>>(),
            Prompt = Prompt,
            Document = Document
        };
    }
}
