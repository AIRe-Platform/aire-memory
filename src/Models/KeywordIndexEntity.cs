using Aire.Sdk.Azure;

namespace Aire.Memory.Models;

/// <summary>
/// PartitionKey = {resourceType}_{keyword}
/// RowKey = {resourceId}
/// </summary>
[EntityTable("KeywordIndex")]
public class KeywordIndexEntity : BaseTableEntity
{
    public string Id()
    {
        return RowKey ?? "";
    }

    public static string? PartitionForResource(string resourceType, string keyword)
    {
        return $"{resourceType}_{keyword}";
    }

    public KeywordIndexEntity() {}
    public KeywordIndexEntity(string resourceType, string resourceId, string keyword)
    {
        PartitionKey = PartitionForResource(resourceType, keyword);
        RowKey = resourceId;
    }
}
