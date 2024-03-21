using Aire.Sdk.Helpers;
using Aire.Sdk.Models.Resources;

namespace Aire.Memory.Models
{
    public class ContentEntity
    {
        public Guid? Id { get; set; } = Guid.NewGuid();
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool? Hidden { get; set; }
        public string? Type { get; set; }
        public string? URL { get; set; }
        public int? views_count { get; set; }
        public int? viewers_rating { get; set; }
        public string? Injured_type { get; set; }
        public string? Age { get; set; }
        public string? Gender { get; set; }
        public DateTime Modified { get; set; }


        public ContentEntity() { }

        
    }
}
