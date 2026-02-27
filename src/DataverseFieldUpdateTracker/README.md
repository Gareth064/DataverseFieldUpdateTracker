# Dataverse Field Update Tracker — C# .NET 8

A C# .NET 8 console application that identifies every business rule, classic workflow, and JavaScript web resource that **sets or modifies a specific field** in Microsoft Dataverse. It combines direct Dataverse API calls with a RAG (Retrieval-Augmented Generation) pipeline powered by Google Gemini AI.

> This is a full port of the original Python implementation. All functionality is preserved; see [dotnet_migration.md](dotnet_migration.md) for the design decisions.

---

## Features

- **Attribute dependency retrieval** — queries Dataverse for all components that depend on a field
- **Business Rule analysis** — parses XAML to detect `SetAttributeValue` / `SetEntityProperty` actions
- **Classic Workflow analysis** — same XAML parsing, includes `UpdateEntity` detection
- **JavaScript web resource analysis** — detects `setValue()` across 5 call styles:
  - `formContext.getAttribute("field").setValue(…)`
  - `formContext.getControl("field").setValue(…)`
  - `Xrm.Page.getAttribute("field").setValue(…)` (deprecated)
  - Variable assignment + later `setValue()` call
  - `executionContext.getFormContext().getAttribute("field").setValue(…)`
- **Gemini-powered RAG responses** — natural-language summaries of matching workflows/scripts
- **Automatic token refresh** — `Azure.Identity` handles OAuth2 token caching and renewal
- **Structured logging** — `Microsoft.Extensions.Logging` throughout; no raw `Console.WriteLine` in services

---

## Prerequisites

| Requirement | Minimum version |
|---|---|
| .NET SDK | 8.0 |
| Dataverse environment | Any with Web API access |
| Azure AD service principal | With Dataverse user role |
| Google Gemini API key | Any tier (free works) |

---

## Installation

### 1. Clone the repository

```bash
git clone https://github.com/Gareth064/DataverseFieldUpdateTracker.git
cd DataverseFieldUpdateTracker
```

### 2. Restore NuGet packages

```bash
dotnet restore
```

### 3. Configure credentials

Create a `.env` file in the directory **from which you run the app** (i.e. next to the `.csproj`, or wherever your working directory is at runtime):

```env
# Azure AD service principal
tenant_id=<your-azure-ad-tenant-id>
client_id=<your-azure-ad-app-client-id>
client_secret=<your-azure-ad-app-secret>

# Dataverse environment URL (must end with /)
env_url=https://<your-org>.crm.dynamics.com/

# Google Gemini API key
GOOGLE_API_KEY=<your-gemini-api-key>
```

The app also reads `SCREAMING_SNAKE_CASE` variants (`TENANT_ID`, `CLIENT_ID`, etc.) if you prefer to set environment variables directly rather than using a `.env` file.

**How to get credentials:**

- **Azure service principal**: [Register an app in Azure AD](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/walkthrough-register-app-azure-active-directory) and grant it the *Dataverse user* role in your environment.
- **Google Gemini API key**: [Get a key from Google AI Studio](https://ai.google.dev/tutorials/setup) — the free tier is sufficient.

---

## Running the app

All commands are run from the repository root unless otherwise noted.

### Interactive mode

Prompts you for the entity and field names at runtime:

```bash
dotnet run --project src/DataverseFieldUpdateTracker
```

```
Please provide entity name: account
Please provide attribute name: emailaddress1
```

### Command-line arguments

Pass entity and attribute directly, useful for scripting:

```bash
dotnet run --project src/DataverseFieldUpdateTracker -- --entity account --attribute emailaddress1
```

```bash
dotnet run --project src/DataverseFieldUpdateTracker -- --entity contact --attribute telephone1
```

### From a published binary

Build a self-contained binary first:

```bash
dotnet publish src/DataverseFieldUpdateTracker -c Release -o ./publish
./publish/DataverseFieldUpdateTracker --entity account --attribute revenue
```

### CLI reference

```
Description:
  Dataverse Field Update Tracker – finds all workflows and web resources that SET a specific field

Usage:
  DataverseFieldUpdateTracker [options]

Options:
  --entity <entity>        Logical entity name (e.g. account, contact)
  --attribute <attribute>  Logical attribute/field name (e.g. name, emailaddress1)
  --version                Show version information
  -?, -h, --help           Show help and usage information
```

---

## What the app does (step by step)

```
1. Authenticate        Azure.Identity ClientSecretCredential → bearer token (auto-refreshed)
        ↓
2. Get attribute ID    GET /api/data/v9.2/EntityDefinitions(LogicalName='…')/Attributes?$filter=…
        ↓
3. Get dependencies    GET /api/data/v9.2/RetrieveDependenciesForDelete(ObjectId=…,ComponentType=2)
        ↓
4. Filter workflows    ServiceClient.Retrieve("workflow", …) for statecode=1, category ∈ {0,2}
        ↓
5. Write wf.txt        JSON-lines file — one workflow record per line
        ↓
6. Get entity forms    QueryExpression on systemform — type 2 (Main) and 6 (Mobile)
        ↓
7. Parse FormXML       Regex extraction of <Library name="…">, <WebResource id="…">, src="…"
        ↓
8. Fetch web resources QueryExpression on webresource, base64-decode JavaScript content
        ↓
9. Write webre.txt     JSON-lines file — one web resource record per line
        ↓
10. RAG — workflows    Load wf.txt → extract XAML actions + attributes → embed → LINQ filter → Gemini
        ↓
11. RAG — web res.     Load webre.txt → extract JS setValue fields → embed → LINQ filter → Gemini
        ↓
12. Print results      Formatted output to stdout
```

---

## Output format

```
================================================================================
DATAVERSE FIELD UPDATE TRACKER (.NET)
================================================================================

1. Finding workflows with SET VALUE / SET DEFAULT actions:
--------------------------------------------------------------------------------
Type: Business Rule, Name: Set Default Email, ID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
Type: Classic Workflow, Name: Sync Email on Update, ID: yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy

================================================================================

2. Finding web resources with SET VALUE actions:
--------------------------------------------------------------------------------
Name: new_account_emailsync.js, ID: zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz
================================================================================
```

---

## Project structure

```
DataverseFieldUpdateTracker/
├── DataverseFieldUpdateTracker.sln
├── .env.example                          # credential template
├── dotnet_migration.md                   # detailed migration design doc
│
├── src/
│   └── DataverseFieldUpdateTracker/
│       ├── DataverseFieldUpdateTracker.csproj
│       ├── Program.cs                    # DI composition root + CLI entry point
│       ├── appsettings.json
│       │
│       ├── Configuration/
│       │   ├── AppSettings.cs            # strongly-typed config with Validate()
│       │   └── DotEnvLoader.cs           # built-in .env parser (no external dep)
│       │
│       ├── Auth/
│       │   ├── IDataverseConnection.cs
│       │   └── DataverseConnection.cs    # Azure.Identity + ServiceClient init
│       │
│       ├── Models/
│       │   ├── WorkflowRecord.cs         # POCO for a workflow/business rule
│       │   ├── WebResourceRecord.cs      # POCO for a JS web resource
│       │   ├── WebResourceReference.cs   # FormXML → web resource name link
│       │   └── DependencyItem.cs         # OData dependency response item
│       │
│       ├── Dataverse/
│       │   ├── IDataverseOperations.cs
│       │   └── DataverseOperations.cs    # HTTP (metadata) + SDK (table) ops
│       │
│       ├── FileIO/
│       │   ├── IFileOperations.cs
│       │   └── FileOperations.cs         # writes wf.txt and webre.txt
│       │
│       ├── Rag/
│       │   ├── Models/
│       │   │   ├── WorkflowDocument.cs   # record type — workflow RAG document
│       │   │   └── WebResourceDocument.cs
│       │   ├── IWorkflowRag.cs
│       │   ├── WorkflowRag.cs            # XAML analysis + Gemini RAG
│       │   ├── IWebResourceRag.cs
│       │   └── WebResourceRag.cs         # JS analysis + Gemini RAG
│       │
│       └── App/
│           └── FieldUpdateTrackerApp.cs  # orchestrates the full pipeline
│
└── tests/
    └── DataverseFieldUpdateTracker.Tests/
        ├── Configuration/                # AppSettings + DotEnvLoader tests
        ├── FileIO/                       # FileOperations tests
        └── Rag/                          # WorkflowRag + WebResourceRag tests
                                          # (59 tests, all passing, no live API calls)
```

---

## NuGet packages

| Package | Purpose |
|---|---|
| `Azure.Identity` 1.13 | OAuth2 service principal auth (auto token refresh) |
| `Microsoft.PowerPlatform.Dataverse.Client` 1.1 | Dataverse SDK — table queries |
| `Microsoft.SemanticKernel` 1.32 | AI orchestration framework |
| `Microsoft.SemanticKernel.Connectors.Google` 1.32 | Gemini chat + `text-embedding-004` |
| `System.CommandLine` 2.0-beta | `--entity` / `--attribute` CLI options |
| `Microsoft.Extensions.Hosting` 8.0 | Generic host + DI container |
| `Microsoft.Extensions.Logging.Console` 8.0 | Structured console logging |

---

## Running the tests

```bash
dotnet test
```

All 59 tests run without network access — Gemini and the Dataverse SDK are mocked via `Moq`.

```
Test Run Successful.
Total tests: 59
     Passed: 59
```

---

## Troubleshooting

| Error | Likely cause | Fix |
|---|---|---|
| `Missing required configuration: TenantId…` | `.env` file not found or key names wrong | Ensure `.env` is in the working directory; check key names match the table above |
| `Dataverse ServiceClient failed to connect` | Wrong credentials or URL | Verify `env_url` ends with `/`; check the service principal has a Dataverse role |
| `Attribute 'x' not found for entity 'y'` | Typo in entity/attribute logical name | Use the logical name (e.g. `emailaddress1`), not the display name |
| `Failed to generate response: … Google API` | Invalid or missing `GOOGLE_API_KEY` | Check the key in `.env`; ensure the Gemini API is enabled in your Google project |
| `Workflow file not found: ./wf.txt` | `InitialiseAsync` called before data retrieval | Always call `RunAsync` (which writes the file) before using RAG directly |

---

## Programmatic usage

You can consume the services directly in your own code by registering them in a DI container:

```csharp
var kernel = Kernel.CreateBuilder()
    .AddGoogleAIGeminiChatCompletion("gemini-2.5-flash", googleApiKey)
    .AddGoogleAIEmbeddingGeneration("text-embedding-004", googleApiKey)
    .Build();

var workflowRag = new WorkflowRag(
    kernel.GetRequiredService<IChatCompletionService>(),
    kernel.GetRequiredService<ITextEmbeddingGenerationService>(),
    logger);

await workflowRag.InitialiseAsync("./wf.txt");

// Find all workflows that SET emailaddress1
string result = await workflowRag.FindSetValueWorkflowsAsync("emailaddress1");
Console.WriteLine(result);

// Find only business rules (category 2)
string brOnly = await workflowRag.FindWorkflowsByTypeAsync("emailaddress1", category: 2);

// Natural language query
string answer = await workflowRag.QueryAsync("Which workflows update the email field?");
```

```csharp
// Web resource equivalent
await webResourceRag.InitialiseAsync("./webre.txt");
string scripts = await webResourceRag.FindSetValueWebResourcesAsync("emailaddress1");
```
