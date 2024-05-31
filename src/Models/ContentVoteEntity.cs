using Aire.Sdk.Azure;

namespace Aire.Memory.Models;

/// <summary>
/// User ID: PartitionKey
/// Content ID: Rowkey
/// </summary>
[EntityTable("ContentVotes")]
public class ContentVoteEntity : BaseTableEntity
{
    public int Value { get; set; }

    public ContentVoteEntity() {}
    public ContentVoteEntity(string userId, string contentId, int value = 0)
    {
        PartitionKey = userId;
        RowKey = contentId;
        Value = value;
    }
}
