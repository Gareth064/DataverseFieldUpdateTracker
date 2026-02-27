using DataverseFieldUpdateTracker.App;
using DataverseFieldUpdateTracker.Auth;
using DataverseFieldUpdateTracker.Configuration;
using DataverseFieldUpdateTracker.Dataverse;
using DataverseFieldUpdateTracker.FileIO;
using DataverseFieldUpdateTracker.Rag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.CommandLine;

// ── Load .env file (development convenience, mirrors python-dotenv) ───────────
DotEnvLoader.Load();

// ── Build host with DI ────────────────────────────────────────────────────────
var builder = Host.CreateApplicationBuilder(args);

// Configuration: appsettings.json + env vars (populated from .env above)
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Map env var names to strongly-typed AppSettings.
// Supports both SCREAMING_SNAKE_CASE and the original Python lowercase key names.
var settings = new AppSettings
{
    TenantId     = builder.Configuration["TENANT_ID"]      ?? builder.Configuration["tenant_id"]      ?? string.Empty,
    ClientId     = builder.Configuration["CLIENT_ID"]      ?? builder.Configuration["client_id"]      ?? string.Empty,
    ClientSecret = builder.Configuration["CLIENT_SECRET"]  ?? builder.Configuration["client_secret"]  ?? string.Empty,
    EnvUrl       = builder.Configuration["ENV_URL"]        ?? builder.Configuration["env_url"]        ?? string.Empty,
    GoogleApiKey = builder.Configuration["GOOGLE_API_KEY"] ?? string.Empty,
};

builder.Services.AddSingleton(settings);

// Logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

// HttpClient factory (used by DataverseOperations for Metadata API calls)
builder.Services.AddHttpClient(nameof(DataverseOperations));

// Dataverse connection + operations
builder.Services.AddSingleton<IDataverseConnection, DataverseConnection>();
builder.Services.AddSingleton<IDataverseOperations, DataverseOperations>();

// File I/O
builder.Services.AddSingleton<IFileOperations, FileOperations>();

// Semantic Kernel – Google Gemini chat + embeddings.
// Registers IChatCompletionService and ITextEmbeddingGenerationService in DI.
builder.Services.AddKernel()
    .AddGoogleAIGeminiChatCompletion(
        modelId: "gemini-2.5-flash",
        apiKey:  settings.GoogleApiKey)
    .AddGoogleAIEmbeddingGeneration(
        modelId: "text-embedding-004",
        apiKey:  settings.GoogleApiKey);

// RAG services
builder.Services.AddSingleton<IWorkflowRag, WorkflowRag>();
builder.Services.AddSingleton<IWebResourceRag, WebResourceRag>();

// App orchestrator
builder.Services.AddSingleton<FieldUpdateTrackerApp>();

var host = builder.Build();

// ── CLI wiring (System.CommandLine) ──────────────────────────────────────────
var entityOption = new Option<string?>(
    "--entity",
    description: "Logical entity name (e.g. account, contact)");

var attributeOption = new Option<string?>(
    "--attribute",
    description: "Logical attribute/field name (e.g. name, emailaddress1)");

var rootCommand = new RootCommand(
    "Dataverse Field Update Tracker – finds all workflows and web resources that SET a specific field")
{
    entityOption,
    attributeOption,
};

rootCommand.SetHandler(async (entity, attribute) =>
{
    // Interactive fallback – mirrors Python input() calls in main.py
    if (string.IsNullOrWhiteSpace(entity))
    {
        Console.Write("Please provide entity name: ");
        entity = Console.ReadLine()?.Trim();
    }

    if (string.IsNullOrWhiteSpace(attribute))
    {
        Console.Write("Please provide attribute name: ");
        attribute = Console.ReadLine()?.Trim();
    }

    if (string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(attribute))
    {
        Console.Error.WriteLine("Error: entity and attribute names are required.");
        Environment.Exit(1);
    }

    var app = host.Services.GetRequiredService<FieldUpdateTrackerApp>();
    try
    {
        await app.RunAsync(entity, attribute);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Environment.Exit(1);
    }

}, entityOption, attributeOption);

return await rootCommand.InvokeAsync(args);
