using Newtonsoft.Json;

namespace Aire.Memory.Models;

public class ContentRating
{
    [JsonProperty("vote", Required = Required.Always)]
    public int? Vote { get; set; }
}
