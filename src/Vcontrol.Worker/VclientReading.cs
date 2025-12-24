using System.Text.Json.Serialization;

namespace Vcontrol.Worker;

public sealed class VclientReading
{
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("raw")]
    public string? Raw { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
