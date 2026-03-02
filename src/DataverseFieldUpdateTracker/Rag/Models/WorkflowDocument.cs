namespace DataverseFieldUpdateTracker.Rag.Models;

/// <summary>
/// In-memory RAG document produced by pre-processing a WorkflowRecord.
/// Holds both the enriched text content (for embedding/search) and
/// the structured metadata (for LINQ-based filtering, equivalent to
/// LlamaIndex MetadataFilters in workflow_rag.py).
/// </summary>
/// <summary>
/// Declared as a record to enable non-destructive mutation via 'with' expressions
/// (used to attach the embedding vector after async generation).
/// </summary>
public sealed record WorkflowDocument
{
    public string WorkflowName { get; init; } = string.Empty;
    public string WorkflowId { get; init; } = string.Empty;

    /// <summary>"0" = Classic Workflow, "2" = Business Rule (stored as string to mirror Python metadata)</summary>
    public string Category { get; init; } = string.Empty;

    public string WorkflowType { get; init; } = string.Empty;

    /// <summary>Pipe-separated action type names, e.g. "SET_VALUE|GET_VALUE"</summary>
    public string Actions { get; init; } = string.Empty;

    /// <summary>Pipe-separated field names being SET, e.g. "emailaddress1|name"</summary>
    public string ModifiedAttributes { get; init; } = string.Empty;

    /// <summary>Pipe-separated field names being GET-read</summary>
    public string ReadAttributes { get; init; } = string.Empty;

    public bool HasSetValue { get; init; }

    /// <summary>Enriched textual content passed to the embedding model.</summary>
    public string EnrichedContent { get; init; } = string.Empty;

    /// <summary>Gemini embedding vector – set after async initialisation.</summary>
    public ReadOnlyMemory<float> Embedding { get; init; }
}
