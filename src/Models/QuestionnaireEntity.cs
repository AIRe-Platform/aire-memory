// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Sdk.Azure;
using Aire.Sdk.Helpers;
using Aire.Sdk.Models.Resources;
using Azure.Storage.Blobs;

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
    public string? EmbeddingId { get; set; }
    public bool IsFeedback { get; set; }

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
        IsFeedback = questionnaire.IsFeedback;
    }

    public async Task<Questionnaire> ToModelAsync(BlobContainerClient client)
    {
        var model = new Questionnaire
        {
            Name = Name,
            Lang = Lang,
            Modified = Timestamp.HasValue ? Timestamp.Value.UtcDateTime : DateTime.UtcNow,
            Keywords = Keywords?.Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            Content = await GetFromBlob(client),
            IsFeedback = IsFeedback
        };

        if (Guid.TryParse(RowKey, out var id))
            model.Id = id;

        return model;
    }

    public async Task<List<QuestionnaireContent>?> GetFromBlob(BlobContainerClient client)
    {
        var blob = client.GetBlobClient(Id());
        if (!blob.Exists())
            return null;

        var stream = await blob.OpenReadAsync();
        var reader = new StreamReader(stream);
        string data = reader.ReadToEnd();

        return data.JsonToObject<List<QuestionnaireContent>?>();
    }

    public async Task SaveToBlob(BlobContainerClient client, List<QuestionnaireContent> content)
    {
        var data = BinaryData.FromString(content.ObjectToJson());
        var blob = client.GetBlobClient(Id());
        await blob.UploadAsync(data, overwrite: true);
    }
}
