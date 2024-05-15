using Newtonsoft.Json;

namespace Aire.Memory.Models;

public class KeywordCreateRequest
{
    [JsonProperty("value", Required = Required.Always)]
    public string? Value { get; set; }
}
