// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


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
