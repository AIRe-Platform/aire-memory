// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Sdk.Azure;
using Aire.Sdk.Helpers;
using Aire.Sdk.Models.Resources;

namespace Aire.Memory.Models;

/// <summary>
/// ID: PartitionKey, Rowkey
/// </summary>
[EntityTable("Contents")]
public class ContentEntity : BaseTableEntity
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? Hidden { get; set; }
    public string? Type { get; set; }
    public int Views { get; set; }
    public int ThumbsUp { get; set; }
    public int ThumbsDown { get; set; }
    public string? Keywords { get; set; }
    public string? URI { get; set; }
    public bool? AddThumbnail { get; set; }
    public string? FileName { get; set; }

    public ContentEntity()
    {
        string id = Guid.NewGuid().ToString();
        PartitionKey ??= id;
        RowKey ??= id;
    }

    public string Id()
    {
        return RowKey ?? "";
    }

    public ContentEntity(Content content)
    {
        var guid = content.Id ?? Guid.NewGuid();

        PartitionKey = guid.ToString();
        RowKey = guid.ToString();

        Name = content.Name;
        Description = content.Description;
        Hidden = content.Hidden;
        Type = content.Type.ObjectToJson();
        Views = content.Views;
        ThumbsUp = content.ThumbsUp;
        ThumbsDown = content.ThumbsDown;
        Keywords = string.Join(",", content.Keywords ?? []);
        AddThumbnail = content.AddThumbnail;
        FileName = content.FileName;
        
        if (content.Type == ContentType.URL)
        {
            if (Uri.TryCreate(content.Url, UriKind.Absolute, out Uri? uri))
            {
                URI = uri.AbsoluteUri;
            }
        }
    }

    public Content ToModel()
    {
        var model = new Content
        {
            Id = Guid.Parse(Id()),
            Name = Name,
            Description = Description,
            Modified = Timestamp.HasValue ? Timestamp.Value.UtcDateTime : DateTime.UtcNow,
            Hidden = Hidden,
            Type = Type?.JsonToObject<ContentType>(),
            Views = Views,
            ThumbsUp = ThumbsUp,
            ThumbsDown = ThumbsDown,
            Score = ThumbsUp - ThumbsDown,
            Keywords = Keywords?.Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            Url = URI,
            AddThumbnail = AddThumbnail,
            FileName = FileName,
        };

        return model;
    }
}

