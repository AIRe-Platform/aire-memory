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
    public int? ViewsCount { get; set; }
    public int? ViewersRating { get; set; }
    public string? Keywords { get; set; }
    public string? URI { get; set; }

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
        ViewsCount = content.ViewsCount;
        ViewersRating = content.ViewersRating;
        Keywords = string.Join(",", content.Keywords!);

        if(Uri.TryCreate(content.Url, UriKind.Absolute, out Uri? uri))
        {
            URI = uri.AbsoluteUri;
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
            ViewsCount = ViewsCount,
            ViewersRating = ViewersRating,
            Keywords = Keywords?.Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            Url = URI,
        };

        return model;
    }
}

