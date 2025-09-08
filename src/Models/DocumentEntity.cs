// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Sdk.Azure;
using Aire.Sdk.Models.Resources;

namespace Aire.Memory.Models;

/// <summary>
/// ID: PartitionKey, Rowkey
/// </summary>
[EntityTable("Documents")]
public class DocumentEntity : BaseTableEntity
{
    public string? Title { get; set; }
    public string? EmbeddingId { get; set; }
    public string? Language { get; set; }
    public string? FileName { get; set; }
    public string? Copyright { get; set; }

    public DocumentEntity()
    {
        string id = Guid.NewGuid().ToString();
        PartitionKey ??= id;
        RowKey ??= id;
    }

    public string Id()
    {
        return RowKey ?? "";
    }

    public DocumentEntity(DocumentMetadata metadata)
    {
        var guid = metadata.Source ?? Guid.NewGuid();
        PartitionKey = guid.ToString();
        RowKey = guid.ToString();

        Title = metadata.Title;
        Language = metadata.Language;
        FileName = metadata.FileName;
        Copyright = metadata.Copyright;
    }

    public DocumentMetadata ToModel()
    {
        var model = new DocumentMetadata
        {
            Source = Guid.Parse(Id()),
            Title = Title,
            Language = Language,
            FileName = FileName,
            Copyright = Copyright,
        };

        return model;
    }
}

