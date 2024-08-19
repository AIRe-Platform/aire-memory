// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Sdk.Models.Resources;
using Aire.Sdk.Azure;
using Azure.Storage.Blobs;
using Aire.Memory.Helpers;

namespace Aire.Memory.Models;

/// <summary>
/// User ID: PartitionKey
/// Result ID: RowKey
/// </summary>
[EntityTable("QuestionnaireResults")]
public class QuestionnaireResultsEntity : BaseTableEntity
{
    public string? QuestionnaireId { get; set; }

    public QuestionnaireResultsEntity() { }
    public QuestionnaireResultsEntity(string userId, string? resultId = null)
    {
        PartitionKey = userId;
        RowKey = resultId ?? Guid.NewGuid().ToString();
    }

    public string Id()
    {
        return RowKey ?? "";
    }

    public string UserId()
    {
        return PartitionKey ?? "";
    }

    public async Task<QuestionnaireResults?> GetFromBlob(BlobContainerClient client, string userKey)
    {
        var blob = client.GetBlobClient(Id());
        if (!blob.Exists())
            return null;

        var stream = await blob.OpenReadAsync();
        var reader = new StreamReader(stream);
        string data = reader.ReadToEnd();

        return EncryptionHelper.DecryptObject<QuestionnaireResults>(data, userKey);
    }

    public async Task SaveToBlob(BlobContainerClient client, QuestionnaireResults results, string userKey)
    {
        var encrypted = EncryptionHelper.EncryptObject(results, userKey);
        var data = BinaryData.FromString(encrypted);
        var blob = client.GetBlobClient(Id());
        await blob.UploadAsync(data, overwrite: true);
    }

    public async Task<QuestionnaireResults> ToModelAsync(BlobContainerClient client, string userKey)
    {
        var content = await GetFromBlob(client, userKey);
        return new QuestionnaireResults
        {
            Id = RowKey,
            QuestionnaireId = QuestionnaireId,
            Timestamp = Timestamp.HasValue ? Timestamp.Value.UtcDateTime : null,
            Answers = content?.Answers,
            Summary = content?.Summary,
            Prompts = content?.Prompts
        };
    }
}
