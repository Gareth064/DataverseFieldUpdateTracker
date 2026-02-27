using System.Text.Json.Serialization;

namespace DataverseFieldUpdateTracker.Models;

/// <summary>
/// Represents an activated workflow or business rule retrieved from Dataverse.
/// Strongly-typed replacement for the raw Python dict returned by
/// retrieve_only_workflowdependency() in dataverse_operations.py.
/// </summary>
public sealed class WorkflowRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("workflowId")]
    public string WorkflowId { get; init; } = string.Empty;

    /// <summary>0 = Classic Workflow, 2 = Business Rule</summary>
    [JsonPropertyName("category")]
    public int Category { get; init; }

    [JsonPropertyName("xaml")]
    public string Xaml { get; init; } = string.Empty;

    /// <summary>1 = Activated</summary>
    [JsonPropertyName("stateCode")]
    public int StateCode { get; init; }
}
