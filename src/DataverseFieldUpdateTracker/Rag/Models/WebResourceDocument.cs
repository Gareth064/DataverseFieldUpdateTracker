namespace DataverseFieldUpdateTracker.Rag.Models;

/// <summary>
/// In-memory RAG document produced by pre-processing a WebResourceRecord.
/// Holds both the enriched text content (for embedding/search) and
/// the structured metadata (for LINQ-based filtering, equivalent to
/// LlamaIndex MetadataFilters in webresource_rag.py).
/// </summary>
/// <summary>
/// Declared as a record to enable non-destructive mutation via 'with' expressions
/// (used to attach the embedding vector after async generation).
/// </summary>
public sealed record WebResourceDocument
{
    public string WebResourceName { get; init; } = string.Empty;
    public string WebResourceId { get; init; } = string.Empty;

    /// <summary>Pipe-separated action type names, e.g. "SET_VALUE"</summary>
    public string Actions { get; init; } = string.Empty;

    /// <summary>Pipe-separated field names modified via setValue(), e.g. "emailaddress1|name"</summary>
    public string ModifiedFields { get; init; } = string.Empty;

    public bool HasSetValue { get; init; }

    /// <summary>Enriched textual content passed to the embedding model.</summary>
    public string EnrichedContent { get; init; } = string.Empty;

    /// <summary>Gemini embedding vector – set after async initialisation.</summary>
    public ReadOnlyMemory<float> Embedding { get; init; }
}
