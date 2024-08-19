// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


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
