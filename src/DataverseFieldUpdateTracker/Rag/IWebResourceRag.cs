namespace DataverseFieldUpdateTracker.Rag;

public interface IWebResourceRag
{
    /// <summary>
    /// Loads and indexes the web resource file. Must be called before any query method.
    /// Replaces DataverseWebResourceRAG.__init__() in webresource_rag.py.
    /// </summary>
    Task InitialiseAsync(string webResourceFilePath = "./webre.txt", CancellationToken ct = default);

    /// <summary>
    /// Finds all web resources that use setValue() on a specific field (case-sensitive).
    /// Replaces find_setvalue_webresources() in webresource_rag.py.
    /// </summary>
    Task<string> FindSetValueWebResourcesAsync(string fieldName, CancellationToken ct = default);

    /// <summary>
    /// Identifies all field updates across all loaded web resources.
    /// Replaces analyze_field_updates() in webresource_rag.py.
    /// </summary>
    Task<string> AnalyzeFieldUpdatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets details about a specific web resource by name.
    /// Replaces get_webresource_by_name() in webresource_rag.py.
    /// </summary>
    Task<string> GetWebResourceByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// General natural-language query against the indexed web resource documents.
    /// Replaces query() in webresource_rag.py.
    /// </summary>
    Task<string> QueryAsync(string question, CancellationToken ct = default);

    /// <summary>
    /// Rebuilds the index from a (potentially updated) web resource file.
    /// Replaces refresh_index() in webresource_rag.py.
    /// </summary>
    Task RefreshIndexAsync(string? webResourceFilePath = null, CancellationToken ct = default);
}
