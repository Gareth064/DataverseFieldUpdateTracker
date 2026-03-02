# .NET Migration Plan: DataverseFieldUpdateTracker

## Overview

This document describes a complete migration of **DataverseFieldUpdateTracker** from Python 3.8+ to a C# .NET 8 console application. The tool tracks field modifications in Microsoft Dataverse by analysing business rules, classic workflows, and JavaScript web resources using a RAG (Retrieval-Augmented Generation) pipeline powered by Google Gemini AI.

---

## Table of Contents

1. [Technology Mapping](#1-technology-mapping)
2. [Solution Structure](#2-solution-structure)
3. [NuGet Dependencies](#3-nuget-dependencies)
4. [Module-by-Module Migration](#4-module-by-module-migration)
   - 4.1 [Authentication – `ConnectToDataverse`](#41-authentication--connecttodataverse)
   - 4.2 [Dataverse Operations – `DataverseOperations`](#42-dataverse-operations--dataverseoperations)
   - 4.3 [File Operations – `FileOperations`](#43-file-operations--fileoperations)
   - 4.4 [Workflow RAG – `WorkflowRag`](#44-workflow-rag--workflowrag)
   - 4.5 [Web Resource RAG – `WebResourceRag`](#45-web-resource-rag--webresourcerag)
   - 4.6 [Entry Point – `Program` / `App`](#46-entry-point--program--app)
5. [Configuration & Secrets](#5-configuration--secrets)
6. [Project File](#6-project-file)
7. [Key Design Decisions](#7-key-design-decisions)
8. [Known Limitations Resolved in Migration](#8-known-limitations-resolved-in-migration)
9. [Migration Steps (ordered)](#9-migration-steps-ordered)
10. [File-by-File Scaffold](#10-file-by-file-scaffold)

---

## 1. Technology Mapping

| Python Component | C# / .NET Equivalent |
|---|---|
| Python 3.8+ | .NET 8 (C# 12) |
| `azure-identity` `ClientSecretCredential` | `Azure.Identity` `ClientSecretCredential` |
| `PowerPlatform-Dataverse-Client` SDK | `Microsoft.PowerPlatform.Dataverse.Client` NuGet |
| `requests` HTTP client | `System.Net.Http.HttpClient` (built-in) |
| `python-dotenv` | `Microsoft.Extensions.Configuration` + `dotenv.net` NuGet (or `.env` loader) |
| `llama-index` vector index + RAG | `Microsoft.SemanticKernel` + `Microsoft.SemanticKernel.Connectors.InMemory` |
| `llama-index-llms-google-genai` | `Microsoft.SemanticKernel.Connectors.Google` (Gemini) |
| `llama-index-embeddings-google-genai` | `Microsoft.SemanticKernel.Connectors.Google` Gemini embeddings |
| `re` (regex) | `System.Text.RegularExpressions.Regex` (built-in) |
| `json` | `System.Text.Json` (built-in) |
| `argparse` CLI | `System.CommandLine` NuGet |
| `print()` logging | `Microsoft.Extensions.Logging` + `ILogger<T>` |
| `unittest` (absent) | `xUnit` + `Moq` (test project) |
| `pyproject.toml` / `requirements.txt` | `*.csproj` + `Directory.Packages.props` |

---

## 2. Solution Structure

```
DataverseFieldUpdateTracker/
├── DataverseFieldUpdateTracker.sln
├── Directory.Packages.props            # Centralised NuGet version pinning
├── .env.example                        # Environment variable template (unchanged)
├── dotnet_migration.md                 # This document
│
├── src/
│   └── DataverseFieldUpdateTracker/    # Main console application
│       ├── DataverseFieldUpdateTracker.csproj
│       ├── appsettings.json            # Non-secret config
│       ├── Program.cs                  # Entry point + DI composition root
│       │
│       ├── Configuration/
│       │   └── AppSettings.cs          # Strongly-typed config model
│       │
│       ├── Auth/
│       │   ├── IDataverseConnection.cs
│       │   └── DataverseConnection.cs  # ← connect_to_dataverse.py
│       │
│       ├── Dataverse/
│       │   ├── IDataverseOperations.cs
│       │   └── DataverseOperations.cs  # ← dataverse_operations.py
│       │
│       ├── FileIO/
│       │   ├── IFileOperations.cs
│       │   └── FileOperations.cs       # ← file_operations.py
│       │
│       ├── Rag/
│       │   ├── IWorkflowRag.cs
│       │   ├── WorkflowRag.cs          # ← workflow_rag.py
│       │   ├── IWebResourceRag.cs
│       │   ├── WebResourceRag.cs       # ← webresource_rag.py
│       │   └── Models/
│       │       ├── WorkflowDocument.cs
│       │       └── WebResourceDocument.cs
│       │
│       ├── Models/
│       │   ├── WorkflowRecord.cs
│       │   └── WebResourceRecord.cs
│       │
│       └── App/
│           └── FieldUpdateTrackerApp.cs # ← main.py orchestration
│
└── tests/
    └── DataverseFieldUpdateTracker.Tests/
        ├── DataverseFieldUpdateTracker.Tests.csproj
        ├── Auth/
        │   └── DataverseConnectionTests.cs
        ├── Dataverse/
        │   └── DataverseOperationsTests.cs
        ├── FileIO/
        │   └── FileOperationsTests.cs
        └── Rag/
            ├── WorkflowRagTests.cs
            └── WebResourceRagTests.cs
```

---

## 3. NuGet Dependencies

### `src/DataverseFieldUpdateTracker/DataverseFieldUpdateTracker.csproj` dependencies

| Package | Version | Replaces |
|---|---|---|
| `Azure.Identity` | 1.12.x | `azure-identity` |
| `Microsoft.PowerPlatform.Dataverse.Client` | 1.1.x | `PowerPlatform-Dataverse-Client` |
| `Microsoft.SemanticKernel` | 1.x | `llama-index` core |
| `Microsoft.SemanticKernel.Connectors.Google` | 1.x | `llama-index-llms-google-genai` + `llama-index-embeddings-google-genai` |
| `Microsoft.SemanticKernel.Connectors.InMemory` | 1.x | LlamaIndex in-process vector store |
| `System.CommandLine` | 2.x | `argparse` |
| `Microsoft.Extensions.Configuration` | 8.x | `python-dotenv` |
| `Microsoft.Extensions.Configuration.EnvironmentVariables` | 8.x | `python-dotenv` |
| `Microsoft.Extensions.Configuration.Json` | 8.x | `python-dotenv` |
| `Microsoft.Extensions.DependencyInjection` | 8.x | Manual instantiation |
| `Microsoft.Extensions.Logging.Console` | 8.x | `print()` statements |
| `dotenv.net` | 3.x | `.env` file loading |

### `tests/` project additional dependencies

| Package | Purpose |
|---|---|
| `xunit` | Test runner |
| `xunit.runner.visualstudio` | IDE integration |
| `Moq` | Mocking interfaces |
| `Microsoft.NET.Test.Sdk` | .NET test SDK |
| `coverlet.collector` | Code coverage |

---

## 4. Module-by-Module Migration

---

### 4.1 Authentication – `ConnectToDataverse`

**Source**: `connect_to_dataverse.py`

**Responsibilities**:
- Load credentials from environment variables
- Create an Azure AD `ClientSecretCredential`
- Initialise a `ServiceClient` (Dataverse SDK)
- Acquire a bearer token for direct HTTP calls

**C# Interface** (`Auth/IDataverseConnection.cs`):

```csharp
public interface IDataverseConnection
{
    ServiceClient ServiceClient { get; }
    string EnvironmentUrl { get; }
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}
```

**C# Implementation** (`Auth/DataverseConnection.cs`):

```csharp
using Azure.Identity;
using Azure.Core;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Extensions.Logging;

public sealed class DataverseConnection : IDataverseConnection
{
    private readonly ClientSecretCredential _credential;
    private readonly ILogger<DataverseConnection> _logger;

    public ServiceClient ServiceClient { get; }
    public string EnvironmentUrl { get; }

    public DataverseConnection(AppSettings settings, ILogger<DataverseConnection> logger)
    {
        _logger = logger;

        // Validate required settings (equivalent to Python's ValueError raises)
        settings.Validate(); // throws InvalidOperationException if missing

        EnvironmentUrl = settings.EnvUrl;

        _credential = new ClientSecretCredential(
            settings.TenantId,
            settings.ClientId,
            settings.ClientSecret);

        var connectionString =
            $"AuthType=ClientSecret;" +
            $"Url={EnvironmentUrl};" +
            $"ClientId={settings.ClientId};" +
            $"ClientSecret={settings.ClientSecret};" +
            $"TenantId={settings.TenantId}";

        ServiceClient = new ServiceClient(connectionString);

        if (!ServiceClient.IsReady)
            throw new InvalidOperationException(
                $"Dataverse ServiceClient failed to connect: {ServiceClient.LastError}");

        _logger.LogInformation("Connected to Dataverse: {Url}", EnvironmentUrl);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var tokenRequestContext = new TokenRequestContext(
            new[] { $"{EnvironmentUrl}/.default" });
        var tokenResult = await _credential.GetTokenAsync(tokenRequestContext, ct);
        return tokenResult.Token;
    }
}
```

**Migration notes**:
- The Python `ConnectionError` / `ValueError` pattern maps to `InvalidOperationException` / `ArgumentException`.
- `ServiceClient` from `Microsoft.PowerPlatform.Dataverse.Client` replaces the Python `DataverseClient`.
- Token is acquired lazily via `GetAccessTokenAsync` rather than at construction time, enabling automatic refresh (fixing a known Python limitation).

---

### 4.2 Dataverse Operations – `DataverseOperations`

**Source**: `dataverse_operations.py`

**Responsibilities**:
- Retrieve attribute metadata ID (GUID) via HTTP Metadata API
- Retrieve dependency list for an attribute
- Filter dependencies for active workflows and business rules
- Retrieve entity forms
- Extract web resource references from FormXML
- Retrieve JavaScript web resource content

**C# Interface** (`Dataverse/IDataverseOperations.cs`):

```csharp
public interface IDataverseOperations
{
    Task<string> GetAttributeIdAsync(string entityName, string attributeName, CancellationToken ct = default);
    Task<JsonDocument> GetDependencyListAsync(string attributeId, CancellationToken ct = default);
    Task<List<WorkflowRecord>> RetrieveWorkflowDependenciesAsync(JsonDocument dependencyList, CancellationToken ct = default);
    Task<List<string>> GetFormsForEntityAsync(string entityName, CancellationToken ct = default);
    Task<List<WebResourceReference>> GetDependencyListForFormsAsync(List<string> formIds, CancellationToken ct = default);
    Task<List<WebResourceRecord>> RetrieveWebResourcesAsync(List<WebResourceReference> references, CancellationToken ct = default);
}
```

**C# Implementation** (`Dataverse/DataverseOperations.cs`) – key method patterns:

```csharp
public sealed class DataverseOperations : IDataverseOperations
{
    private readonly IDataverseConnection _connection;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DataverseOperations> _logger;
    private const string ApiVersion = "9.2";

    // ── HTTP helper ──────────────────────────────────────────────────────────
    private async Task<HttpClient> GetAuthorisedClientAsync(CancellationToken ct)
    {
        var token = await _connection.GetAccessTokenAsync(ct);
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return _httpClient;
    }

    // ── GetAttributeIdAsync ──────────────────────────────────────────────────
    // Replaces: get_attibuteid()
    // Uses: HTTP GET EntityDefinitions metadata endpoint
    public async Task<string> GetAttributeIdAsync(
        string entityName, string attributeName, CancellationToken ct = default)
    {
        var client = await GetAuthorisedClientAsync(ct);
        var url = $"{_connection.EnvironmentUrl}/api/data/v{ApiVersion}/" +
                  $"EntityDefinitions(LogicalName='{entityName}')" +
                  $"/Attributes(LogicalName='{attributeName}')";

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.GetProperty("MetadataId").GetString()
               ?? throw new InvalidOperationException("MetadataId not found in response.");
    }

    // ── RetrieveWorkflowDependenciesAsync ────────────────────────────────────
    // Replaces: retrieve_only_workflowdependency()
    // Uses: ServiceClient (SDK) for individual workflow record retrieval
    public async Task<List<WorkflowRecord>> RetrieveWorkflowDependenciesAsync(
        JsonDocument dependencyList, CancellationToken ct = default)
    {
        var results = new List<WorkflowRecord>();
        var value = dependencyList.RootElement.GetProperty("value");

        foreach (var dep in value.EnumerateArray())
        {
            if (dep.GetProperty("ComponentType").GetInt32() != 29) continue;

            var category = dep.GetProperty("DependentComponentObjectId")
                             .GetString(); // parsed further below
            // ... retrieve workflow via ServiceClient.Retrieve("workflow", id, cols)
            // filter statecode == 1, category in {0, 2}
        }

        return results;
    }

    // ── GetFormsForEntityAsync ───────────────────────────────────────────────
    // Replaces: get_forms_for_entity()
    // Uses: ServiceClient QueryExpression with pagination

    // ── GetDependencyListForFormsAsync ───────────────────────────────────────
    // Replaces: get_dependencylist_for_form()
    // Regex patterns for FormXML unchanged; uses Regex class

    // ── RetrieveWebResourcesAsync ────────────────────────────────────────────
    // Replaces: retrieve_webresources_from_dependency()
    // Base64 decode: Convert.FromBase64String() → Encoding.UTF8.GetString()
}
```

**Migration notes**:
- All Python `requests.get()` HTTP calls become `HttpClient.GetAsync()` with `EnsureSuccessStatusCode()`.
- Python `client.get("workflow", record_id=..., select=[...])` becomes `ServiceClient.Retrieve("workflow", id, new ColumnSet(...))`.
- Pagination via Python SDK generators becomes `QueryExpression` with `PagingInfo` or `RetrieveMultipleRequest`.
- Regex patterns from Python (`re.findall`, `re.search`) map directly to `Regex.Matches()` / `Regex.Match()`.
- `base64.b64decode()` becomes `Convert.FromBase64String()`.

---

### 4.3 File Operations – `FileOperations`

**Source**: `file_operations.py`

**Responsibilities**:
- Serialise workflow list to `wf.txt` (one JSON object per line)
- Serialise web resource list to `webre.txt` (one JSON object per line)
- Strip OData metadata keys (keys containing `@`)

**C# Interface** (`FileIO/IFileOperations.cs`):

```csharp
public interface IFileOperations
{
    Task WriteWorkflowFileAsync(IEnumerable<WorkflowRecord> workflows,
                                string path = "./wf.txt",
                                CancellationToken ct = default);

    Task WriteWebResourceFileAsync(IEnumerable<WebResourceRecord> resources,
                                   string path = "./webre.txt",
                                   CancellationToken ct = default);
}
```

**C# Implementation** (`FileIO/FileOperations.cs`):

```csharp
public sealed class FileOperations : IFileOperations
{
    private readonly ILogger<FileOperations> _logger;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = false
    };

    public async Task WriteWorkflowFileAsync(
        IEnumerable<WorkflowRecord> workflows,
        string path = "./wf.txt",
        CancellationToken ct = default)
    {
        await using var writer = new StreamWriter(path, append: false,
            encoding: System.Text.Encoding.UTF8);

        foreach (var wf in workflows)
        {
            // Serialise; OData '@' keys are not present on strongly-typed records
            var line = JsonSerializer.Serialize(wf, _jsonOpts);
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }

        _logger.LogInformation("Wrote {Count} workflows to {Path}", workflows.Count(), path);
    }

    // WriteWebResourceFileAsync follows identical pattern
}
```

**Migration notes**:
- Python's OData key filtering (`if '@' in key`) is eliminated because the C# model objects (`WorkflowRecord`, `WebResourceRecord`) only contain typed properties — there are no raw dictionary entries.
- `open(..., encoding='utf-8')` becomes `StreamWriter` with `Encoding.UTF8`.
- Async I/O via `StreamWriter.WriteLineAsync` is used throughout.

---

### 4.4 Workflow RAG – `WorkflowRag`

**Source**: `workflow_rag.py`

**Responsibilities**:
- Load `wf.txt` and preprocess XAML into enriched `Document` objects
- Build / load a persistent vector index
- Answer queries about which workflows SET/modify a specific field

**RAG Technology Replacement**:

| Python (LlamaIndex) | C# (Semantic Kernel) |
|---|---|
| `VectorStoreIndex` | `InMemoryVectorStore` + `VectorStoreTextSearch` |
| `SentenceSplitter` (chunk_size=512, overlap=100) | `TextChunker` from `SemanticKernel.Text` |
| `GoogleGenAIEmbedding` | `GoogleAIEmbeddingGenerator` (Gemini `text-embedding-004`) |
| `GoogleGenAI` LLM | `GoogleAITextGenerationService` (Gemini `gemini-2.5-flash`) |
| `MetadataFilters` | LINQ filtering on `IAsyncEnumerable<VectorSearchResult>` |
| `QueryEngine.query()` | `VectorStoreTextSearch.SearchAsync()` + `IChatCompletionService.GetChatMessageContentsAsync()` |
| `persist_dir` (disk cache) | `JsonFileVectorStore` or `SqliteVectorStore` |

**C# Interface** (`Rag/IWorkflowRag.cs`):

```csharp
public interface IWorkflowRag
{
    Task InitialiseAsync(string workflowFilePath = "./wf.txt", CancellationToken ct = default);
    Task<string> FindSetValueWorkflowsAsync(string fieldName, CancellationToken ct = default);
    Task<string> FindWorkflowsByTypeAsync(string fieldName, int category, CancellationToken ct = default);
    Task<string> QueryAsync(string question, CancellationToken ct = default);
    Task RefreshIndexAsync(CancellationToken ct = default);
}
```

**C# Implementation sketch** (`Rag/WorkflowRag.cs`):

```csharp
public sealed class WorkflowRag : IWorkflowRag
{
    private readonly Kernel _kernel;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<WorkflowRag> _logger;

    // XAML action keywords – exact mirror of Python ACTION_KEYWORDS dict
    private static readonly Dictionary<string, string[]> ActionKeywords = new()
    {
        ["SET_VALUE"]       = ["mcwc:SetAttributeValue", "mxswa:SetEntityProperty", "SetAttributeValueStep"],
        ["SET_DEFAULT"]     = ["mcwc:SetDefaultValue", "SetDefaultValue"],
        ["GET_VALUE"]       = ["mxswa:GetEntityProperty", "GetEntityProperty"],
        ["SET_DISPLAY_MODE"]= ["mcwc:SetDisplayMode", "SetDisplayMode"],
        ["SHOW_HIDE"]       = ["mcwc:SetVisibility", "SetVisibility"],
        ["LOCK_UNLOCK"]     = ["SetRequiredLevel", "IsReadOnly"],
        ["UPDATE_ENTITY"]   = ["mxswa:UpdateEntity"],
    };

    // Regex for modified attributes – mirrors Python pattern exactly
    private static readonly Regex ModifiedAttrRegex = new(
        @"<mxswa:SetEntityProperty[^>]+Attribute=""([^""]+)""",
        RegexOptions.Compiled);

    // Regex for read attributes
    private static readonly Regex ReadAttrRegex = new(
        @"<mxswa:GetEntityProperty[^>]+Attribute=""([^""]+)""",
        RegexOptions.Compiled);

    public async Task<string> FindSetValueWorkflowsAsync(
        string fieldName, CancellationToken ct = default)
    {
        // 1. Search vector store for documents where modified_attributes contains fieldName
        //    and has_set_value == true
        var collection = _vectorStore.GetCollection<string, WorkflowDocument>("workflows");
        var searchService = new VectorStoreTextSearch<WorkflowDocument>(collection, ...);

        var results = await searchService.SearchAsync(fieldName, top: 20, ct);

        // 2. Filter in-memory: modified_attributes.Contains(fieldName) && has_set_value
        var matching = results
            .Where(r => r.Record.ModifiedAttributes
                          .Split('|', StringSplitOptions.RemoveEmptyEntries)
                          .Contains(fieldName, StringComparer.Ordinal)
                     && r.Record.HasSetValue)
            .ToList();

        if (!matching.Any())
            return $"No workflows found that SET the field '{fieldName}'.";

        // 3. Build prompt and call Gemini to generate formatted response
        var prompt = BuildSetValuePrompt(fieldName, matching);
        var chat = _kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddUserMessage(prompt);
        var response = await chat.GetChatMessageContentsAsync(history, cancellationToken: ct);
        return response.Last().Content ?? string.Empty;
    }

    private static string ExtractXamlActions(string xaml) { /* ... */ }
    private static List<string> ExtractModifiedAttributes(string xaml)
        => ModifiedAttrRegex.Matches(xaml)
                            .Select(m => m.Groups[1].Value)
                            .Distinct()
                            .ToList();
    private static List<string> ExtractReadAttributes(string xaml)
        => ReadAttrRegex.Matches(xaml)
                        .Select(m => m.Groups[1].Value)
                        .Distinct()
                        .ToList();
}
```

**Persistence strategy**:
- For Phase 1, use `InMemoryVectorStore` (rebuilt on each run, equivalent to the Python default without `persist_dir`).
- For Phase 2, persist the vector store to a local SQLite database via `Microsoft.SemanticKernel.Connectors.Sqlite` or serialise to JSON on disk.

---

### 4.5 Web Resource RAG – `WebResourceRag`

**Source**: `webresource_rag.py`

**Responsibilities**:
- Load `webre.txt` and preprocess JavaScript into enriched `Document` objects
- Build / load a persistent vector index
- Detect `setValue()` calls on specific fields via regex

**C# Interface** (`Rag/IWebResourceRag.cs`):

```csharp
public interface IWebResourceRag
{
    Task InitialiseAsync(string webResourceFilePath = "./webre.txt", CancellationToken ct = default);
    Task<string> FindSetValueWebResourcesAsync(string fieldName, CancellationToken ct = default);
    Task<string> QueryAsync(string question, CancellationToken ct = default);
    Task RefreshIndexAsync(CancellationToken ct = default);
}
```

**JavaScript regex patterns** (direct translation of Python patterns):

```csharp
private static readonly Regex[] SetValuePatterns =
[
    // Pattern 1: formContext.getAttribute("field").setValue(
    new(@"formContext\.\s*getAttribute\s*\(\s*[""'](\w+)[""']\s*\)\s*\.\s*setValue\s*\(",
        RegexOptions.Compiled),

    // Pattern 2: formContext.getControl("field").setValue(
    new(@"formContext\.\s*getControl\s*\(\s*[""'](\w+)[""']\s*\)\s*\.\s*setValue\s*\(",
        RegexOptions.Compiled),

    // Pattern 3: Xrm.Page.getAttribute("field").setValue(
    new(@"Xrm\.\s*Page\.\s*getAttribute\s*\(\s*[""'](\w+)[""']\s*\)\s*\.\s*setValue\s*\(",
        RegexOptions.Compiled),

    // Pattern 4: executionContext.getFormContext().getAttribute("field").setValue(
    new(@"executionContext\.\s*getFormContext\s*\(\s*\)\.\s*getAttribute\s*\(\s*[""'](\w+)[""']\s*\)\s*\.\s*setValue\s*\(",
        RegexOptions.Compiled),
];
```

**Migration notes**:
- Variable-assignment tracking (Python Pattern 4) is best reproduced using a two-pass regex: first capture `let varName = formContext.getAttribute("field")`, then find `varName.setValue(`. This is a direct port; no library change needed.
- `has_set_value` metadata flag maps to a `bool` property on the `WebResourceDocument` model.

---

### 4.6 Entry Point – `Program` / `App`

**Source**: `main.py`

**C# `Program.cs`** (top-level statements + DI setup):

```csharp
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Load .env file (dev only)
DotNetEnv.Env.Load();

// Configuration
builder.Configuration.AddEnvironmentVariables();
builder.Services.Configure<AppSettings>(builder.Configuration);

// Register services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IDataverseConnection, DataverseConnection>();
builder.Services.AddSingleton<IDataverseOperations, DataverseOperations>();
builder.Services.AddSingleton<IFileOperations, FileOperations>();
builder.Services.AddSingleton<IWorkflowRag, WorkflowRag>();
builder.Services.AddSingleton<IWebResourceRag, WebResourceRag>();
builder.Services.AddSingleton<FieldUpdateTrackerApp>();

var host = builder.Build();

// System.CommandLine wiring
var entityOption  = new Option<string>("--entity",  "Logical entity name (e.g. account)")  { IsRequired = false };
var attrOption    = new Option<string>("--attribute","Logical attribute name (e.g. name)")  { IsRequired = false };
var rootCommand   = new RootCommand("Dataverse Field Update Tracker") { entityOption, attrOption };

rootCommand.SetHandler(async (entity, attribute) =>
{
    // Interactive fallback mirrors Python behaviour
    if (string.IsNullOrWhiteSpace(entity))
    {
        Console.Write("Enter entity name: ");
        entity = Console.ReadLine()!.Trim();
    }
    if (string.IsNullOrWhiteSpace(attribute))
    {
        Console.Write("Enter attribute name: ");
        attribute = Console.ReadLine()!.Trim();
    }

    var app = host.Services.GetRequiredService<FieldUpdateTrackerApp>();
    await app.RunAsync(entity, attribute);

}, entityOption, attrOption);

return await rootCommand.InvokeAsync(args);
```

**`App/FieldUpdateTrackerApp.cs`** – orchestration equivalent of `main.py`:

```csharp
public sealed class FieldUpdateTrackerApp
{
    private readonly IDataverseOperations _ops;
    private readonly IFileOperations _fileOps;
    private readonly IWorkflowRag _workflowRag;
    private readonly IWebResourceRag _webResourceRag;
    private readonly ILogger<FieldUpdateTrackerApp> _logger;

    public async Task RunAsync(string entityName, string attributeName, CancellationToken ct = default)
    {
        await GenerateMetadataFilesAsync(entityName, attributeName, ct);
        await RunRagAnalysisAsync(attributeName, ct);
    }

    private async Task GenerateMetadataFilesAsync(string entityName, string attributeName, CancellationToken ct)
    {
        var attrId        = await _ops.GetAttributeIdAsync(entityName, attributeName, ct);
        var depList       = await _ops.GetDependencyListAsync(attrId, ct);
        var workflows     = await _ops.RetrieveWorkflowDependenciesAsync(depList, ct);
        await _fileOps.WriteWorkflowFileAsync(workflows, ct: ct);

        var formIds       = await _ops.GetFormsForEntityAsync(entityName, ct);
        var webRefs       = await _ops.GetDependencyListForFormsAsync(formIds, ct);
        var webResources  = await _ops.RetrieveWebResourcesAsync(webRefs, ct);
        await _fileOps.WriteWebResourceFileAsync(webResources, ct: ct);
    }

    private async Task RunRagAnalysisAsync(string attributeName, CancellationToken ct)
    {
        await _workflowRag.InitialiseAsync(ct: ct);
        await _webResourceRag.InitialiseAsync(ct: ct);

        var wfResult  = await _workflowRag.FindSetValueWorkflowsAsync(attributeName, ct);
        var wrResult  = await _webResourceRag.FindSetValueWebResourcesAsync(attributeName, ct);

        Console.WriteLine("=== Workflow Results ===");
        Console.WriteLine(wfResult);
        Console.WriteLine("\n=== Web Resource Results ===");
        Console.WriteLine(wrResult);
    }
}
```

---

## 5. Configuration & Secrets

**`Configuration/AppSettings.cs`**:

```csharp
public sealed class AppSettings
{
    public string TenantId      { get; init; } = string.Empty;
    public string ClientId      { get; init; } = string.Empty;
    public string ClientSecret  { get; init; } = string.Empty;
    public string EnvUrl        { get; init; } = string.Empty;
    public string GoogleApiKey  { get; init; } = string.Empty;

    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(TenantId))     missing.Add(nameof(TenantId));
        if (string.IsNullOrWhiteSpace(ClientId))     missing.Add(nameof(ClientId));
        if (string.IsNullOrWhiteSpace(ClientSecret)) missing.Add(nameof(ClientSecret));
        if (string.IsNullOrWhiteSpace(EnvUrl))       missing.Add(nameof(EnvUrl));
        if (string.IsNullOrWhiteSpace(GoogleApiKey)) missing.Add(nameof(GoogleApiKey));

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Missing required configuration: {string.Join(", ", missing)}");
    }
}
```

**`appsettings.json`** (non-secret defaults only):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

Environment variables (loaded from `.env` in development via `dotenv.net`):

```
TENANT_ID=
CLIENT_ID=
CLIENT_SECRET=
ENV_URL=
GOOGLE_API_KEY=
```

`IConfiguration` maps env vars to `AppSettings` properties automatically via `AddEnvironmentVariables()`.

---

## 6. Project File

**`src/DataverseFieldUpdateTracker/DataverseFieldUpdateTracker.csproj`**:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
    <AssemblyName>DataverseFieldUpdateTracker</AssemblyName>
    <RootNamespace>DataverseFieldUpdateTracker</RootNamespace>
    <Version>0.2.0</Version>
    <Description>Track field updates in Microsoft Dataverse</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Azure.Identity" />
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" />
    <PackageReference Include="Microsoft.SemanticKernel" />
    <PackageReference Include="Microsoft.SemanticKernel.Connectors.Google" />
    <PackageReference Include="Microsoft.SemanticKernel.Connectors.InMemory" />
    <PackageReference Include="Microsoft.SemanticKernel.Plugins.Memory" />
    <PackageReference Include="System.CommandLine" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" />
    <PackageReference Include="dotenv.net" />
  </ItemGroup>

</Project>
```

**`tests/DataverseFieldUpdateTracker.Tests/DataverseFieldUpdateTracker.Tests.csproj`**:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Moq" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\DataverseFieldUpdateTracker\DataverseFieldUpdateTracker.csproj" />
  </ItemGroup>

</Project>
```

---

## 7. Key Design Decisions

### 7.1 Dependency Injection Throughout
All Python classes that instantiate their own dependencies (`ConnectToDataverse`, `DataverseOperations`) are converted to constructor-injected services registered in `IServiceCollection`. This enables:
- Testability via `Moq` mocks
- Clean lifecycle management (`Singleton` / `Scoped`)
- Replacement of real services with fakes in tests

### 7.2 Interface-First Design
Each service has a corresponding `I*` interface. This was implicit in Python (duck-typing) but is made explicit in C# to support mocking and future substitution.

### 7.3 Async/Await Throughout
All I/O-bound operations (HTTP, SDK calls, file writes) use `async`/`await` with `CancellationToken` propagation. This resolves the Python codebase's synchronous blocking I/O pattern and enables:
- Cancellation support
- Concurrent metadata retrieval (e.g., `Task.WhenAll` for multiple form lookups)

### 7.4 Strongly-Typed Models Over Dictionaries
Python used raw `dict` objects (with OData metadata keys requiring manual filtering). C# uses:
- `WorkflowRecord` — POCO for workflow metadata
- `WebResourceRecord` — POCO for web resource metadata
- `WorkflowDocument` — RAG vector document with metadata properties
- `WebResourceDocument` — RAG vector document with metadata properties

OData `@` key filtering is completely eliminated.

### 7.5 Structured Logging Over print()
`ILogger<T>` replaces all `print()` statements. Log levels map as:
- `print("...")` → `_logger.LogInformation(...)`
- `print("Warning: ...")` → `_logger.LogWarning(...)`
- `print("Error: ...")` → `_logger.LogError(...)`

### 7.6 Semantic Kernel as LlamaIndex Replacement
Microsoft Semantic Kernel is the closest equivalent to LlamaIndex in the .NET ecosystem. Key mappings:
- `VectorStoreTextSearch` replaces `QueryEngine`
- `ITextSearch` with metadata LINQ filters replaces `MetadataFilters`
- `KernelFunction` / prompt templates replace `LLM.complete()` calls
- `InMemoryVectorStore` replaces the default in-process LlamaIndex index

### 7.7 Token Refresh Resolved
The Python limitation ("access tokens expire after 1 hour, no auto-refresh") is automatically resolved by using `Azure.Identity`'s `ClientSecretCredential`, which caches and refreshes tokens transparently. `GetAccessTokenAsync` is called before each HTTP request.

---

## 8. Known Limitations Resolved in Migration

| Python Limitation | C# Resolution |
|---|---|
| Access tokens expire after 1 hour (no refresh) | `Azure.Identity` handles refresh automatically |
| No unit tests | `xUnit` + `Moq` test project included |
| `print()` statements instead of logging | `Microsoft.Extensions.Logging` with structured logs |
| Synchronous blocking I/O | `async`/`await` throughout |
| Raw dicts with OData key leakage | Strongly-typed POCO models |
| No cancellation support | `CancellationToken` on all async methods |
| No dependency injection (manual construction) | `IServiceCollection` DI throughout |

---

## 9. Migration Steps (ordered)

1. **Create solution and project files**
   ```bash
   dotnet new sln -n DataverseFieldUpdateTracker
   dotnet new console -n DataverseFieldUpdateTracker -o src/DataverseFieldUpdateTracker --framework net8.0
   dotnet new xunit -n DataverseFieldUpdateTracker.Tests -o tests/DataverseFieldUpdateTracker.Tests
   dotnet sln add src/DataverseFieldUpdateTracker/DataverseFieldUpdateTracker.csproj
   dotnet sln add tests/DataverseFieldUpdateTracker.Tests/DataverseFieldUpdateTracker.Tests.csproj
   ```

2. **Add NuGet packages**
   ```bash
   cd src/DataverseFieldUpdateTracker
   dotnet add package Azure.Identity
   dotnet add package Microsoft.PowerPlatform.Dataverse.Client
   dotnet add package Microsoft.SemanticKernel
   dotnet add package Microsoft.SemanticKernel.Connectors.Google --prerelease
   dotnet add package Microsoft.SemanticKernel.Connectors.InMemory --prerelease
   dotnet add package System.CommandLine --prerelease
   dotnet add package Microsoft.Extensions.Hosting
   dotnet add package Microsoft.Extensions.Logging.Console
   dotnet add package dotenv.net
   ```

3. **Implement `Configuration/AppSettings.cs`** (no external dependencies, start here)

4. **Implement `Auth/DataverseConnection.cs`** + write unit tests with mocked `ServiceClient`

5. **Implement model POCOs** (`Models/WorkflowRecord.cs`, `Models/WebResourceRecord.cs`)

6. **Implement `FileIO/FileOperations.cs`** + unit tests (no Dataverse dependency, easy to test)

7. **Implement `Dataverse/DataverseOperations.cs`** — largest module; implement and test method by method:
   - `GetAttributeIdAsync` (HTTP only, mock `HttpClient`)
   - `GetDependencyListAsync` (HTTP only)
   - `RetrieveWorkflowDependenciesAsync` (SDK + filtering)
   - `GetFormsForEntityAsync` (SDK + pagination)
   - `GetDependencyListForFormsAsync` (SDK + regex)
   - `RetrieveWebResourcesAsync` (SDK + base64 decode)

8. **Implement RAG document models** (`Rag/Models/WorkflowDocument.cs`, `Rag/Models/WebResourceDocument.cs`)

9. **Implement `Rag/WorkflowRag.cs`** — port XAML regex patterns first, test independently, then wire up Semantic Kernel

10. **Implement `Rag/WebResourceRag.cs`** — port JavaScript regex patterns first, then wire up Semantic Kernel

11. **Implement `App/FieldUpdateTrackerApp.cs`** (orchestration — all dependencies already built)

12. **Wire up `Program.cs`** (DI composition root + `System.CommandLine`)

13. **Integration test** against a real Dataverse sandbox environment

14. **Add `Directory.Packages.props`** for centralised version pinning

15. **Remove Python files** (`*.py`, `requirements.txt`, `pyproject.toml`) once .NET implementation is verified

---

## 10. File-by-File Scaffold

The following table maps every Python source file to its C# replacement(s):

| Python File | C# File(s) | Notes |
|---|---|---|
| `connect_to_dataverse.py` | `Auth/IDataverseConnection.cs`<br>`Auth/DataverseConnection.cs` | |
| `dataverse_operations.py` | `Dataverse/IDataverseOperations.cs`<br>`Dataverse/DataverseOperations.cs`<br>`Models/WorkflowRecord.cs`<br>`Models/WebResourceRecord.cs`<br>`Models/WebResourceReference.cs` | Split model types out |
| `file_operations.py` | `FileIO/IFileOperations.cs`<br>`FileIO/FileOperations.cs` | |
| `workflow_rag.py` | `Rag/IWorkflowRag.cs`<br>`Rag/WorkflowRag.cs`<br>`Rag/Models/WorkflowDocument.cs` | |
| `webresource_rag.py` | `Rag/IWebResourceRag.cs`<br>`Rag/WebResourceRag.cs`<br>`Rag/Models/WebResourceDocument.cs` | |
| `main.py` | `Program.cs`<br>`App/FieldUpdateTrackerApp.cs` | |
| `example_usage.py` | _(removed)_ | Replaced by xUnit integration tests |
| `requirements.txt` | `DataverseFieldUpdateTracker.csproj` | |
| `pyproject.toml` | `DataverseFieldUpdateTracker.csproj` | |
| `.env.example` | `.env.example` (unchanged) | |
| `README.md` | `README.md` (update SDK/install instructions) | |
| `AGENTS.md` | `AGENTS.md` (update for C# patterns) | |
| `ERROR_HANDLING.md` | `ERROR_HANDLING.md` (update exception types) | |

---

*Migration plan authored for the `claude/dotnet-migration-plan-mnnMM` branch.*
