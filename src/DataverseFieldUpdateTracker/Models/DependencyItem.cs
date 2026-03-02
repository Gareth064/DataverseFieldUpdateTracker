using System.Text.Json.Serialization;

namespace DataverseFieldUpdateTracker.Models;

/// <summary>
/// Represents a single item in the dependency list returned by
/// the Dataverse RetrieveDependenciesForDelete OData function.
/// Property names match the lowercase OData response keys.
/// </summary>
public sealed class DependencyItem
{
    [JsonPropertyName("dependentcomponenttype")]
    public int DependentComponentType { get; init; }

    [JsonPropertyName("dependentcomponentobjectid")]
    public string DependentComponentObjectId { get; init; } = string.Empty;

    [JsonPropertyName("dependencytype")]
    public int DependencyType { get; init; }

    [JsonPropertyName("requiredcomponenttype")]
    public int RequiredComponentType { get; init; }

    [JsonPropertyName("requiredcomponentobjectid")]
    public string RequiredComponentObjectId { get; init; } = string.Empty;
}
