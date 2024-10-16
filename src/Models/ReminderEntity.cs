// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Memory.Helpers;
using Aire.Sdk.Azure;
using Aire.Sdk.Models.Resources;
using Azure.Storage.Blobs;

namespace Aire.Memory.Models;

/// <summary>
/// PartitionKey: userId
/// RowKey: reminderId
/// </summary>
[EntityTable("Reminders")]
public class ReminderEntity : BaseTableEntity
{
    public long? TriggerTimestamp { get; set; }
    public long? ReadTimestamp { get; set; }

    public ReminderEntity() { }

    public ReminderEntity(string userId, string? eventId = null)
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

    public ReminderEntity(Reminder reminder, string userId)
    {
        var guid = reminder.Id ?? Guid.NewGuid();
        
        PartitionKey = userId;
        RowKey = guid.ToString();
        TriggerTimestamp = reminder.TriggerTimestamp;
        ReadTimestamp = reminder.ReadTimestamp;
    }

    public Reminder ToModel()
    {
        var model = new Reminder
        {
            Id = Guid.Parse(Id()),
            TriggerTimestamp = TriggerTimestamp,
            ReadTimestamp = ReadTimestamp
        };

        return model;
    }

    public async Task<ReminderContent?> GetContentFromBlob(BlobContainerClient client, string userKey)
    {
        var blob = client.GetBlobClient(Id());
        if (!blob.Exists())
            return null;

        var stream = await blob.OpenReadAsync();
        var reader = new StreamReader(stream);
        string data = reader.ReadToEnd();

        var content = EncryptionHelper.DecryptObject<ReminderContent>(data, userKey);
        return content;
    }

    public async Task SaveContentToBlob(BlobContainerClient client, ReminderContent scheduledEventContent, string userKey)
    {
        var encrypted = EncryptionHelper.EncryptObject(scheduledEventContent, userKey);
        var data = BinaryData.FromString(encrypted);
        var blob = client.GetBlobClient(Id());
        await blob.UploadAsync(data, overwrite: true);
    }
}

