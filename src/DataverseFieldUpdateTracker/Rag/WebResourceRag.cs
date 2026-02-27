using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataverseFieldUpdateTracker.Models;
using DataverseFieldUpdateTracker.Rag.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;

namespace DataverseFieldUpdateTracker.Rag;

/// <summary>
/// RAG system for analysing Dataverse JavaScript web resources.
/// Replaces webresource_rag.py – DataverseWebResourceRAG class.
///
/// Strategy:
///   • find_setvalue_webresources: pure LINQ metadata filter
///     (mirrors Python MetadataFilters – no vector search required)
///   • query(): semantic search via Gemini embeddings + cosine similarity,
///     then LLM-generated response from top-k results.
/// </summary>
public sealed class WebResourceRag : IWebResourceRag
{
    // JavaScript action keywords (ported from webresource_rag.py ACTION_KEYWORDS)
    private static readonly string[] SetValueKeywords = [".setValue(", "setAttribute"];

    // JavaScript setValue patterns (all 5 ported directly from webresource_rag.py)
    private static readonly Regex[] SetValuePatterns =
    [
        // Pattern 1: formContext.getAttribute("field").setValue(
        new(@"formContext\.\s*getAttribute\s*\(\s*[""']([\w]+)[""']\s*\)\s*\.\s*setValue\s*\(",
            RegexOptions.Compiled),

        // Pattern 2: formContext.getControl("field").setValue(
        new(@"formContext\.\s*getControl\s*\(\s*[""']([\w]+)[""']\s*\)\s*\.\s*setValue\s*\(",
            RegexOptions.Compiled),

        // Pattern 3: Xrm.Page.getAttribute("field").setValue(
        new(@"Xrm\.\s*Page\.\s*getAttribute\s*\(\s*[""']([\w]+)[""']\s*\)\s*\.\s*setValue\s*\(",
            RegexOptions.Compiled),

        // Pattern 5: executionContext.getFormContext().getAttribute("field").setValue(
        new(@"executionContext\.\s*getFormContext\s*\(\s*\)\.\s*getAttribute\s*\(\s*[""']([\w]+)[""']\s*\)\s*\.\s*setValue\s*\(",
            RegexOptions.Compiled),
    ];

    // Pattern 4a: variable assignment  var/let/const x = formContext.getAttribute("field")
    private static readonly Regex VarAssignmentPattern = new(
        @"(?:var|let|const)\s+(\w+)\s*=\s*(?:formContext|executionContext\.\s*getFormContext\s*\(\s*\))\.\s*getAttribute\s*\(\s*[""']([\w]+)[""']\s*\)",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
    };

    private readonly IChatCompletionService _chatService;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly ILogger<WebResourceRag> _logger;

    private List<WebResourceDocument> _documents = [];
    private string _currentFilePath = "./webre.txt";

    public WebResourceRag(
        IChatCompletionService chatService,
        ITextEmbeddingGenerationService embeddingService,
        ILogger<WebResourceRag> logger)
    {
        _chatService      = chatService;
        _embeddingService = embeddingService;
        _logger           = logger;
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    public async Task InitialiseAsync(
        string webResourceFilePath = "./webre.txt", CancellationToken ct = default)
    {
        _currentFilePath = webResourceFilePath;

        if (!File.Exists(webResourceFilePath))
            throw new FileNotFoundException(
                $"Web resource file not found: {webResourceFilePath}. " +
                "Please run the data retrieval step first to generate this file.");

        _documents = await BuildDocumentsAsync(webResourceFilePath, ct);
        _logger.LogInformation(
            "WebResourceRag initialised with {Count} documents", _documents.Count);
    }

    public async Task RefreshIndexAsync(string? webResourceFilePath = null, CancellationToken ct = default)
    {
        _currentFilePath = webResourceFilePath ?? _currentFilePath;
        _documents = await BuildDocumentsAsync(_currentFilePath, ct);
        _logger.LogInformation("WebResourceRag index refreshed – {Count} documents", _documents.Count);
    }

    // ── Pre-processing (mirrors _preprocess_webresources in Python) ───────────

    private async Task<List<WebResourceDocument>> BuildDocumentsAsync(
        string filePath, CancellationToken ct)
    {
        string fileContent;
        try { fileContent = await File.ReadAllTextAsync(filePath, ct); }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Failed to read web resource file '{filePath}': {ex.Message}", ex);
        }

        var docs = new List<WebResourceDocument>();

        foreach (var line in fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            WebResourceRecord? record;
            try { record = JsonSerializer.Deserialize<WebResourceRecord>(trimmed, JsonOpts); }
            catch (JsonException ex)
            {
                _logger.LogWarning("Skipping invalid web resource JSON line: {Error}", ex.Message);
                continue;
            }

            if (record is null) continue;

            var actions        = ExtractJavaScriptActions(record.DecodedContent);
            var modifiedFields = ExtractFieldsModified(record.DecodedContent);
            var hasSetValue    = actions.Contains("SET_VALUE");

            var content = BuildEnrichedContent(record, actions, modifiedFields);

            docs.Add(new WebResourceDocument
            {
                WebResourceName = record.Name,
                WebResourceId   = record.Id,
                Actions         = string.Join('|', actions),
                ModifiedFields  = string.Join('|', modifiedFields),
                HasSetValue     = hasSetValue,
                EnrichedContent = content,
            });

            _logger.LogInformation(
                "Processed [Web Resource]: {Name} | Modified: [{Fields}]",
                record.Name, string.Join(", ", modifiedFields));
        }

        // Graceful empty handling: return with a placeholder (mirrors Python behaviour)
        if (docs.Count == 0)
        {
            _logger.LogWarning("No web resources found in file. Index will be empty.");
            return docs;
        }

        // Generate embeddings in one batch
        var texts = docs.Select(d => d.EnrichedContent).ToList();
        try
        {
            var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
                texts, cancellationToken: ct);

            for (int i = 0; i < docs.Count; i++)
                docs[i] = docs[i] with { Embedding = embeddings[i] };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to generate embeddings (semantic search disabled): {Message}", ex.Message);
        }

        return docs;
    }

    // ── Primary query methods ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> FindSetValueWebResourcesAsync(
        string fieldName, CancellationToken ct = default)
    {
        EnsureInitialised();

        // LINQ metadata filter – mirrors Python MetadataFilters CONTAINS + EQ
        // Case-sensitive field name matching (mirrors Python behaviour)
        var matching = _documents
            .Where(d => d.HasSetValue &&
                        d.ModifiedFields
                         .Split('|', StringSplitOptions.RemoveEmptyEntries)
                         .Any(f => string.Equals(f, fieldName, StringComparison.Ordinal)))
            .ToList();

        if (matching.Count == 0)
            return "No webresources found";

        var prompt =
            $"List all web resources that use setValue() to modify the field '{fieldName}'.\n" +
            $"For each web resource, provide: name and ID.\n" +
            $"Return format: Name: <webresource_name>, ID: <webresource_id>.\n" +
            $"If no web resources are found, return 'No webresources found'.\n\n" +
            $"Context:\n{BuildContextFromDocuments(matching)}";

        var result = await CallLlmAsync(prompt, ct);

        // Preserve Python behaviour: normalise "none"/"no web resource" to canonical string
        if (string.IsNullOrWhiteSpace(result) ||
            result.Contains("no web resource", StringComparison.OrdinalIgnoreCase) ||
            result.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return "No webresources found";
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<string> AnalyzeFieldUpdatesAsync(CancellationToken ct = default)
    {
        return await QueryAsync(
            "What field updates occur in these web resources? " +
            "List all fields that are modified with setValue().", ct);
    }

    /// <inheritdoc/>
    public async Task<string> GetWebResourceByNameAsync(string name, CancellationToken ct = default)
    {
        return await QueryAsync(
            $"What actions are performed in the web resource named '{name}'? " +
            "Include the ID and all fields modified.", ct);
    }

    /// <inheritdoc/>
    public async Task<string> QueryAsync(string question, CancellationToken ct = default)
    {
        EnsureInitialised();

        if (_documents.Count == 0)
            return "No web resources are indexed.";

        var topDocs = await FindTopDocumentsAsync(question, topK: 3, ct);

        var context = topDocs.Count > 0
            ? BuildContextFromDocuments(topDocs)
            : BuildContextFromDocuments(_documents.Take(3).ToList());

        var prompt = $"{question}\n\nContext:\n{context}";
        return await CallLlmAsync(prompt, ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void EnsureInitialised()
    {
        if (_documents.Count == 0 && !File.Exists(_currentFilePath))
            throw new InvalidOperationException(
                "WebResourceRag has not been initialised. Call InitialiseAsync() first.");
    }

    private async Task<List<WebResourceDocument>> FindTopDocumentsAsync(
        string query, int topK, CancellationToken ct)
    {
        if (_documents.All(d => d.Embedding.IsEmpty))
            return _documents.Take(topK).ToList();

        ReadOnlyMemory<float> queryEmbedding;
        try
        {
            var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
                new[] { query }, cancellationToken: ct);
            queryEmbedding = embeddings[0];
        }
        catch
        {
            return _documents.Take(topK).ToList();
        }

        return _documents
            .Where(d => !d.Embedding.IsEmpty)
            .Select(d => (doc: d, score: CosineSimilarity(queryEmbedding, d.Embedding)))
            .OrderByDescending(x => x.score)
            .Take(topK)
            .Select(x => x.doc)
            .ToList();
    }

    private async Task<string> CallLlmAsync(string prompt, CancellationToken ct)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(
            "You are a helpful assistant that analyses Microsoft Dataverse JavaScript web resources. " +
            "Answer concisely and factually based on the provided context.");
        history.AddUserMessage(prompt);

        try
        {
            var response = await _chatService.GetChatMessageContentsAsync(
                history, cancellationToken: ct);
            return response.LastOrDefault()?.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError("LLM call failed: {Message}", ex.Message);
            throw new InvalidOperationException(
                $"Failed to generate response: {ex.Message}. " +
                "Please verify your Google API key and network connection.", ex);
        }
    }

    private static string BuildContextFromDocuments(IEnumerable<WebResourceDocument> docs)
    {
        var sb = new StringBuilder();
        foreach (var d in docs)
        {
            sb.AppendLine(d.EnrichedContent);
            sb.AppendLine("---");
        }
        return sb.ToString();
    }

    private static string BuildEnrichedContent(
        WebResourceRecord wr, List<string> actions, List<string> modifiedFields)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WEB RESOURCE TYPE: JavaScript");
        sb.AppendLine($"NAME: {wr.Name}");
        sb.AppendLine($"WEB RESOURCE ID: {wr.Id}");
        sb.AppendLine();
        sb.AppendLine($"ACTIONS: {(actions.Count > 0 ? string.Join(", ", actions) : "None detected")}");
        sb.AppendLine($"MODIFIED FIELDS (setValue): {(modifiedFields.Count > 0 ? string.Join(", ", modifiedFields) : "None")}");
        sb.AppendLine();
        sb.AppendLine("ACTION DETAILS:");

        if (actions.Contains("SET_VALUE"))
        {
            sb.AppendLine("- Sets/updates field values using setValue()");
            sb.AppendLine($"  Fields Modified: {string.Join(", ", modifiedFields)}");
        }

        sb.AppendLine();
        sb.AppendLine("JavaScript Code (excerpt):");
        sb.AppendLine(wr.DecodedContent.Length > 1500
            ? wr.DecodedContent[..1500]
            : wr.DecodedContent);

        return sb.ToString();
    }

    // ── JavaScript extraction (ported from webresource_rag.py) ───────────────

    internal static List<string> ExtractJavaScriptActions(string jsCode)
    {
        var actions = new List<string>();
        if (SetValueKeywords.Any(k => jsCode.Contains(k, StringComparison.Ordinal)))
            actions.Add("SET_VALUE");
        return actions;
    }

    /// <summary>
    /// Extracts field names modified via setValue(). Ports all 5 Python patterns.
    /// Case-sensitive matching (mirrors Python behaviour).
    /// </summary>
    internal static List<string> ExtractFieldsModified(string jsCode)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);

        // Patterns 1–3 + 5 (direct chained calls)
        foreach (var pattern in SetValuePatterns)
        {
            foreach (Match m in pattern.Matches(jsCode))
                fields.Add(m.Groups[1].Value);
        }

        // Pattern 4: variable assignment tracking (two-pass)
        var varMatches = VarAssignmentPattern.Matches(jsCode);
        foreach (Match varMatch in varMatches)
        {
            var varName   = varMatch.Groups[1].Value;
            var fieldName = varMatch.Groups[2].Value;

            // Check if the variable is later called with .setValue(
            var setValueCall = new Regex(
                $@"{Regex.Escape(varName)}\.\s*setValue\s*\(", RegexOptions.None);
            if (setValueCall.IsMatch(jsCode))
                fields.Add(fieldName);
        }

        return fields.ToList();
    }

    // ── Cosine similarity ─────────────────────────────────────────────────────

    private static float CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var aSpan = a.Span;
        var bSpan = b.Span;
        int len = Math.Min(aSpan.Length, bSpan.Length);

        float dot = 0f, magA = 0f, magB = 0f;
        for (int i = 0; i < len; i++)
        {
            dot  += aSpan[i] * bSpan[i];
            magA += aSpan[i] * aSpan[i];
            magB += bSpan[i] * bSpan[i];
        }

        var denom = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return denom == 0f ? 0f : dot / denom;
    }
}
