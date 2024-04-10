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
    public Guid? Id { get; set; } = Guid.NewGuid();
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? Hidden { get; set; }
    public string? Type { get; set; }
    public string? Url { get; set; }
    public int? ViewsCount { get; set; }
    public int? ViewersRating { get; set; }
    public string? InjuredType { get; set; }
    public string? Age { get; set; }
    public string? Gender { get; set; }

    public ContentEntity() 
    { 
        string id = Guid.NewGuid().ToString();
        PartitionKey ??= id;
        RowKey ??= id;
    }

    public ContentEntity(Content content) 
    { 
        PartitionKey = content.Id.ToString();
        RowKey = content.Id.ToString();
        Name = content.Name;
        Description = content.Description;
        Hidden = content.Hidden;
        Type = content.Type;
        Url = content.Url; 
        ViewsCount = content.ViewsCount;
        ViewersRating = content.ViewersRating;
        InjuredType = content.InjuredType;
        Age = content.Age;
        Gender = content.Gender;
    }

    public Content ToModel()
    {
        var model = new Content
        {
            Name = Name,
            Description = Description,
            Modified = Timestamp.HasValue ? Timestamp.Value.UtcDateTime : DateTime.UtcNow,
            Hidden = Hidden,
            Type = Type,
            Url = Url, 
            ViewsCount = ViewsCount,
            ViewersRating = ViewersRating,
            InjuredType = InjuredType,
            Age = Age,
            Gender = Gender
        };

        if (Guid.TryParse(RowKey, out var id))
            model.Id = id;

        return model;
    }
}

