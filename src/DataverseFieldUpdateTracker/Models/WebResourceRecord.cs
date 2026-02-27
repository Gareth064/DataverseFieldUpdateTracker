using System.Text.Json.Serialization;

namespace DataverseFieldUpdateTracker.Models;

/// <summary>
/// Represents a JavaScript web resource retrieved from Dataverse.
/// Strongly-typed replacement for the raw Python dict returned by
/// retrieve_webresources_from_dependency() in dataverse_operations.py.
/// </summary>
public sealed class WebResourceRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("decodedContent")]
    public string DecodedContent { get; init; } = string.Empty;
}
