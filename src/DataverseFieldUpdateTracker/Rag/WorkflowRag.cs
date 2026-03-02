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
/// RAG system for analysing Dataverse workflow XAML.
/// Replaces workflow_rag.py – DataverseWorkflowRAG class.
///
/// Strategy:
///   • find_set_value_workflows / find_workflows_by_type: pure LINQ metadata filter
///     (mirrors Python MetadataFilters – no vector search required because the
///     metadata is already extracted and stored on each document)
///   • query(): semantic search via Gemini embeddings + cosine similarity, then
///     LLM-generated response from the top-k results.
/// </summary>
public sealed class WorkflowRag : IWorkflowRag
{
    // ── XAML action keywords – exact mirror of Python ACTION_KEYWORDS dict ────
    private static readonly Dictionary<string, string[]> ActionKeywords = new()
    {
        ["SET_VALUE"]        = ["mcwc:SetAttributeValue", "mxswa:SetEntityProperty", "SetAttributeValueStep"],
        ["SET_DEFAULT"]      = ["mcwc:SetDefaultValue", "SetDefaultValue"],
        ["GET_VALUE"]        = ["mxswa:GetEntityProperty", "GetEntityProperty"],
        ["SET_DISPLAY_MODE"] = ["mcwc:SetDisplayMode", "SetDisplayMode"],
        ["SHOW_HIDE"]        = ["mcwc:SetVisibility", "SetVisibility"],
        ["LOCK_UNLOCK"]      = ["SetRequiredLevel", "IsReadOnly"],
        ["UPDATE_ENTITY"]    = ["mxswa:UpdateEntity"],
    };

    private static readonly Dictionary<int, string> CategoryNames = new()
    {
        [0] = "Classic Workflow",
        [2] = "Business Rule",
    };

    // Regex patterns ported directly from workflow_rag.py
    private static readonly Regex ModifiedAttrRegex = new(
        @"<mxswa:SetEntityProperty[^>]+Attribute=""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex ReadAttrRegex = new(
        @"<mxswa:GetEntityProperty[^>]+Attribute=""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
    };

    private readonly IChatCompletionService _chatService;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly ILogger<WorkflowRag> _logger;

    private List<WorkflowDocument> _documents = [];
    private string _currentFilePath = "./wf.txt";

    public WorkflowRag(
        IChatCompletionService chatService,
        ITextEmbeddingGenerationService embeddingService,
        ILogger<WorkflowRag> logger)
    {
        _chatService      = chatService;
        _embeddingService = embeddingService;
        _logger           = logger;
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    public async Task InitialiseAsync(
        string workflowFilePath = "./wf.txt", CancellationToken ct = default)
    {
        _currentFilePath = workflowFilePath;

        if (!File.Exists(workflowFilePath))
            throw new FileNotFoundException(
                $"Workflow file not found: {workflowFilePath}. " +
                "Please run the data retrieval step first to generate this file.");

        _documents = await BuildDocumentsAsync(workflowFilePath, ct);
        _logger.LogInformation("Workflow RAG initialised with {Count} documents", _documents.Count);
    }

    public async Task RefreshIndexAsync(string? workflowFilePath = null, CancellationToken ct = default)
    {
        _currentFilePath = workflowFilePath ?? _currentFilePath;
        _documents = await BuildDocumentsAsync(_currentFilePath, ct);
        _logger.LogInformation("Workflow RAG index refreshed – {Count} documents", _documents.Count);
    }

    // ── Pre-processing (mirrors _preprocess_workflows in Python) ─────────────

    private async Task<List<WorkflowDocument>> BuildDocumentsAsync(
        string filePath, CancellationToken ct)
    {
        string fileContent;
        try { fileContent = await File.ReadAllTextAsync(filePath, ct); }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Failed to read workflow file '{filePath}': {ex.Message}", ex);
        }

        var docs = new List<WorkflowDocument>();

        foreach (var line in fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            WorkflowRecord? record;
            try { record = JsonSerializer.Deserialize<WorkflowRecord>(trimmed, JsonOpts); }
            catch (JsonException ex)
            {
                _logger.LogWarning("Skipping invalid workflow JSON line: {Error}", ex.Message);
                continue;
            }

            if (record is null) continue;

            var actions       = ExtractXamlActions(record.Xaml);
            var modifiedAttrs = ExtractModifiedAttributes(record.Xaml);
            var readAttrs     = ExtractReadAttributes(record.Xaml);
            var wfType        = GetWorkflowType(record.Category);
            var hasSetValue   = actions.Contains("SET_VALUE") || actions.Contains("SET_DEFAULT");

            var content = BuildEnrichedContent(record, wfType, actions, modifiedAttrs, readAttrs);

            var doc = new WorkflowDocument
            {
                WorkflowName        = record.Name,
                WorkflowId          = record.WorkflowId,
                Category            = record.Category.ToString(),
                WorkflowType        = wfType,
                Actions             = string.Join('|', actions),
                ModifiedAttributes  = string.Join('|', modifiedAttrs),
                ReadAttributes      = string.Join('|', readAttrs),
                HasSetValue         = hasSetValue,
                EnrichedContent     = content,
            };

            docs.Add(doc);
            _logger.LogInformation(
                "Processed [{Type}]: {Name} | Modified: [{Fields}]",
                wfType, record.Name, string.Join(", ", modifiedAttrs));
        }

        if (docs.Count == 0)
            throw new InvalidOperationException(
                $"No valid workflow records found in '{filePath}'. " +
                "The file may be empty or contain invalid data.");

        // Generate embeddings for all documents in one batch for efficiency
        var texts = docs.Select(d => d.EnrichedContent).ToList();
        IList<ReadOnlyMemory<float>> embeddings;
        try
        {
            embeddings = await _embeddingService.GenerateEmbeddingsAsync(texts, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to generate embeddings (semantic search disabled): {Message}", ex.Message);
            return docs; // Return docs without embeddings; metadata filtering still works
        }

        for (int i = 0; i < docs.Count; i++)
            docs[i] = docs[i] with { Embedding = embeddings[i] };

        return docs;
    }

    // ── Primary query methods ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> FindSetValueWorkflowsAsync(
        string fieldName, CancellationToken ct = default)
    {
        EnsureInitialised();

        // LINQ metadata filter – mirrors Python MetadataFilters with CONTAINS + EQ
        var matching = _documents
            .Where(d => d.HasSetValue &&
                        d.ModifiedAttributes
                         .Split('|', StringSplitOptions.RemoveEmptyEntries)
                         .Any(a => string.Equals(a, fieldName, StringComparison.Ordinal)))
            .ToList();

        if (matching.Count == 0)
            return $"No workflows found that SET the field '{fieldName}'.";

        var prompt =
            $"List all workflows (business rules and classic workflows) that set or modify the field '{fieldName}'.\n" +
            $"For each workflow, provide: workflow type, name, and ID.\n" +
            $"Return format: Type: <workflow_type>, Name: <workflow_name>, ID: <workflow_id>\n\n" +
            $"Context:\n{BuildContextFromDocuments(matching)}";

        return await CallLlmAsync(prompt, ct);
    }

    /// <inheritdoc/>
    public async Task<string> FindWorkflowsByTypeAsync(
        string fieldName, int category, CancellationToken ct = default)
    {
        EnsureInitialised();

        var matching = _documents
            .Where(d => d.HasSetValue &&
                        d.Category == category.ToString() &&
                        d.ModifiedAttributes
                         .Split('|', StringSplitOptions.RemoveEmptyEntries)
                         .Any(a => string.Equals(a, fieldName, StringComparison.Ordinal)))
            .ToList();

        var wfType = GetWorkflowType(category);

        if (matching.Count == 0)
            return $"No {wfType}s found that SET the field '{fieldName}'.";

        var prompt =
            $"List all {wfType}s that set or modify the field '{fieldName}'.\n" +
            $"Return format: Name: <workflow_name>, ID: <workflow_id>\n\n" +
            $"Context:\n{BuildContextFromDocuments(matching)}";

        return await CallLlmAsync(prompt, ct);
    }

    /// <inheritdoc/>
    public async Task<string> AnalyzeFieldUpdatesAsync(CancellationToken ct = default)
    {
        return await QueryAsync(
            "What field updates occur in these workflows? " +
            "List all record attributes that are modified with workflow type, name, and ID.", ct);
    }

    /// <inheritdoc/>
    public async Task<string> AnalyzeBusinessRulesAsync(CancellationToken ct = default)
    {
        return await QueryAsync(
            "What business rule actions are defined? " +
            "How do they impact form behaviour or data integrity?", ct);
    }

    /// <inheritdoc/>
    public async Task<string> GetWorkflowByNameAsync(string name, CancellationToken ct = default)
    {
        return await QueryAsync(
            $"What actions are performed in the workflow named '{name}'? " +
            "Include the workflow type, ID, and all actions.", ct);
    }

    /// <inheritdoc/>
    public async Task<string> QueryAsync(string question, CancellationToken ct = default)
    {
        EnsureInitialised();

        // Semantic search: find top-3 most relevant documents by cosine similarity
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
        if (_documents.Count == 0)
            throw new InvalidOperationException(
                "WorkflowRag has not been initialised. Call InitialiseAsync() first.");
    }

    private async Task<List<WorkflowDocument>> FindTopDocumentsAsync(
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
            "You are a helpful assistant that analyses Microsoft Dataverse automation. " +
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

    private static string BuildContextFromDocuments(IEnumerable<WorkflowDocument> docs)
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
        WorkflowRecord wf, string wfType,
        List<string> actions, List<string> modifiedAttrs, List<string> readAttrs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"WORKFLOW TYPE: {wfType}");
        sb.AppendLine($"NAME: {wf.Name}");
        sb.AppendLine($"WORKFLOW ID: {wf.WorkflowId}");
        sb.AppendLine($"CATEGORY: {wf.Category}");
        sb.AppendLine();
        sb.AppendLine($"ACTIONS: {(actions.Count > 0 ? string.Join(", ", actions) : "None detected")}");
        sb.AppendLine($"MODIFIED ATTRIBUTES (SET): {(modifiedAttrs.Count > 0 ? string.Join(", ", modifiedAttrs) : "None")}");
        sb.AppendLine($"READ ATTRIBUTES (GET): {(readAttrs.Count > 0 ? string.Join(", ", readAttrs) : "None")}");
        sb.AppendLine();
        sb.AppendLine("ACTION DETAILS:");

        if (actions.Contains("SET_VALUE") || actions.Contains("SET_DEFAULT"))
        {
            sb.AppendLine("- Sets/updates field values (SetAttributeValue/SetEntityProperty/SetDefaultValue)");
            sb.AppendLine($"  Fields Modified: {string.Join(", ", modifiedAttrs)}");
        }
        if (actions.Contains("UPDATE_ENTITY"))
            sb.AppendLine("- Executes UpdateEntity operation (Classic Workflow)");
        if (actions.Contains("GET_VALUE"))
        {
            sb.AppendLine("- Reads field values (GetEntityProperty)");
            sb.AppendLine($"  Fields Read: {string.Join(", ", readAttrs)}");
        }
        if (actions.Contains("SET_DISPLAY_MODE"))
            sb.AppendLine("- Changes field display modes");

        // Add XAML excerpt (first 1500 chars, matching Python behaviour)
        sb.AppendLine();
        sb.AppendLine("XAML (excerpt):");
        sb.AppendLine(wf.Xaml.Length > 1500 ? wf.Xaml[..1500] : wf.Xaml);

        return sb.ToString();
    }

    // ── XAML extraction (ported from workflow_rag.py) ─────────────────────────

    internal static List<string> ExtractXamlActions(string xaml)
    {
        var actions = new List<string>();
        foreach (var (actionType, keywords) in ActionKeywords)
        {
            if (keywords.Any(k => xaml.Contains(k, StringComparison.Ordinal)))
                actions.Add(actionType);
        }
        return actions;
    }

    internal static List<string> ExtractModifiedAttributes(string xaml) =>
        ModifiedAttrRegex.Matches(xaml)
                         .Select(m => m.Groups[1].Value)
                         .Distinct()
                         .ToList();

    internal static List<string> ExtractReadAttributes(string xaml) =>
        ReadAttrRegex.Matches(xaml)
                     .Select(m => m.Groups[1].Value)
                     .Distinct()
                     .ToList();

    internal static string GetWorkflowType(int category) =>
        CategoryNames.TryGetValue(category, out var name)
            ? name
            : $"Unknown Type (Category {category})";

    // ── Cosine similarity (used for semantic search) ──────────────────────────

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
