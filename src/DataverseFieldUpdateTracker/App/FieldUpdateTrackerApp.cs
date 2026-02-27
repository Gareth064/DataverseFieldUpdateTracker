using DataverseFieldUpdateTracker.Dataverse;
using DataverseFieldUpdateTracker.FileIO;
using DataverseFieldUpdateTracker.Rag;
using Microsoft.Extensions.Logging;

namespace DataverseFieldUpdateTracker.App;

/// <summary>
/// Orchestrates the full field-update tracking pipeline.
/// Replaces main.py – DataverseFieldUpdateTrackerApp class.
///
/// Steps:
///   1. Retrieve attribute ID and dependency list from Dataverse.
///   2. Write wf.txt (workflows) and webre.txt (web resources).
///   3. Initialise RAG agents and run analysis.
///   4. Print results.
/// </summary>
public sealed class FieldUpdateTrackerApp
{
    private readonly IDataverseOperations _dvOps;
    private readonly IFileOperations _fileOps;
    private readonly IWorkflowRag _workflowRag;
    private readonly IWebResourceRag _webResourceRag;
    private readonly ILogger<FieldUpdateTrackerApp> _logger;

    public FieldUpdateTrackerApp(
        IDataverseOperations dvOps,
        IFileOperations fileOps,
        IWorkflowRag workflowRag,
        IWebResourceRag webResourceRag,
        ILogger<FieldUpdateTrackerApp> logger)
    {
        _dvOps          = dvOps;
        _fileOps        = fileOps;
        _workflowRag    = workflowRag;
        _webResourceRag = webResourceRag;
        _logger         = logger;
    }

    /// <summary>Full pipeline: fetch data, generate files, and analyse.</summary>
    public async Task RunAsync(
        string entityName, string attributeName, CancellationToken ct = default)
    {
        await GenerateMetadataFilesAsync(entityName, attributeName, ct);
        await RunRagAnalysisAsync(attributeName, ct);
    }

    // ── Step 1: Fetch from Dataverse and write metadata files ─────────────────

    private async Task GenerateMetadataFilesAsync(
        string entityName, string attributeName, CancellationToken ct)
    {
        _logger.LogInformation(
            "Generating metadata files for {Entity}.{Attribute}", entityName, attributeName);

        // Get attribute ID
        var attributeId = await _dvOps.GetAttributeIdAsync(entityName, attributeName, ct);

        // Get and filter dependencies for workflows/business rules
        var depList   = await _dvOps.GetDependencyListAsync(attributeId, ct);
        var workflows = await _dvOps.RetrieveWorkflowDependenciesAsync(depList, ct);
        await _fileOps.WriteWorkflowFileAsync(workflows, ct: ct);

        // Get forms and extract web resource references
        var formIds   = await _dvOps.GetFormsForEntityAsync(entityName, ct);
        var webRefs   = await _dvOps.GetWebResourceReferencesFromFormsAsync(formIds, ct);
        var webResources = await _dvOps.RetrieveWebResourcesAsync(webRefs, ct);
        await _fileOps.WriteWebResourceFileAsync(webResources, ct: ct);
    }

    // ── Step 2: RAG analysis ──────────────────────────────────────────────────

    private async Task RunRagAnalysisAsync(string attributeName, CancellationToken ct)
    {
        Console.WriteLine(new string('=', 80));
        Console.WriteLine("DATAVERSE FIELD UPDATE TRACKER (.NET)");
        Console.WriteLine(new string('=', 80));

        // Initialise RAG agents (loads files and generates embeddings)
        await _workflowRag.InitialiseAsync(ct: ct);
        await _webResourceRag.InitialiseAsync(ct: ct);

        Console.WriteLine("\n1. Finding workflows with SET VALUE / SET DEFAULT actions:");
        Console.WriteLine(new string('-', 80));
        var wfResult = await _workflowRag.FindSetValueWorkflowsAsync(attributeName, ct);
        Console.WriteLine(wfResult);

        Console.WriteLine(new string('=', 80));
        Console.WriteLine("\n2. Finding web resources with SET VALUE actions:");
        Console.WriteLine(new string('-', 80));
        var wrResult = await _webResourceRag.FindSetValueWebResourcesAsync(attributeName, ct);
        Console.WriteLine(wrResult);

        Console.WriteLine(new string('=', 80));
    }
}
