// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Memory.Helpers;
using Aire.Sdk.Azure;
using Aire.Sdk.Models.Resources;
using Azure.Storage.Blobs;

namespace Aire.Memory.Models;

/// <summary>
/// ID: PartitionKey, Rowkey
/// </summary>
[EntityTable("Events")]
public class ScheduledEventEntity : BaseTableEntity
{
    public long? TriggerTimestamp { get; set; }

    public long? ReadTimestamp { get; set; }

    public ScheduledEventEntity() { }

    public ScheduledEventEntity(string userId, string? eventId = null)
    {
        PartitionKey = userId;
        RowKey = eventId ?? Guid.NewGuid().ToString();
    }

    public string Id()
    {
        return RowKey ?? "";
    }

    public string UserId()
    {
        return PartitionKey ?? "";
    }

    public ScheduledEventEntity(ScheduledEvent scheduledEvent, string userId)
    {
        var guid = scheduledEvent.Id ?? Guid.NewGuid();

        PartitionKey = userId;
        RowKey = guid.ToString();

        TriggerTimestamp = scheduledEvent.TriggerTimestamp;
        ReadTimestamp = scheduledEvent.ReadTimestamp;
    }

    public ScheduledEvent ToModel()
    {
        var model = new ScheduledEvent
        {
            Id = Guid.Parse(Id()),
            TriggerTimestamp = TriggerTimestamp,
            ReadTimestamp = ReadTimestamp
        };

        return model;
    }

    public async Task<ScheduledEventContent?> GetContentFromBlob(BlobContainerClient client, string userKey)
    {
        var blob = client.GetBlobClient(Id());
        if (!blob.Exists())
            return null;

        var stream = await blob.OpenReadAsync();
        var reader = new StreamReader(stream);
        string data = reader.ReadToEnd();

        var scheduledEventContent = EncryptionHelper.DecryptObject<ScheduledEventContent>(data, userKey);
        return scheduledEventContent;
    }

    public async Task SaveContentToBlob(BlobContainerClient client, ScheduledEventContent scheduledEventContent, string userKey)
    {
        var encrypted = EncryptionHelper.EncryptObject(scheduledEventContent, userKey);
        var data = BinaryData.FromString(encrypted);
        var blob = client.GetBlobClient(Id());
        await blob.UploadAsync(data, overwrite: true);
    }
}

