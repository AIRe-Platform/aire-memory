using Aire.Memory.Helpers;
using Aire.Sdk.Azure;
using Aire.Sdk.Models.Chat;
using Azure.Storage.Blobs;

namespace Aire.Memory.Models;

/// <summary>
/// User ID: PartitionKey
/// Chat ID: RowKey
/// </summary>
[EntityTable("Chatlogs")]
public class ChatLogEntity : BaseTableEntity
{
    public ChatLogEntity() { }
    public ChatLogEntity(string userId, string? chatId = null)
    {
        PartitionKey = userId;
        RowKey = chatId ?? Guid.NewGuid().ToString();
    }

    public string Id()
    {
        return RowKey ?? "";
    }

    public string UserId()
    {
        return PartitionKey ?? "";
    }

    public async Task<ChatLog?> GetFromBlob(BlobContainerClient client, string userKey)
    {
        var blob = client.GetBlobClient(Id());
        if (!blob.Exists())
            return null;

        var stream = await blob.OpenReadAsync();
        var reader = new StreamReader(stream);
        string data = reader.ReadToEnd();

        var chat = EncryptionHelper.DecryptObject<ChatLog>(data, userKey);
        if (chat == null)
        {
            // Backwards-compatibility with message lists
            var messages = EncryptionHelper.DecryptObject<List<ChatMessage>>(data, userKey);
            if (messages != null)
            {
                chat = new ChatLog { Messages = messages };
            }
        }
        return chat;
    }

    public async Task SaveToBlob(BlobContainerClient client, ChatLog chat, string userKey)
    {
        var data = EncryptionHelper.EncryptObject(chat, userKey);
        var blob = client.GetBlobClient(Id());
        await blob.UploadAsync(data, overwrite: true);
    }
}
