using System.Text.Json;
using DataverseFieldUpdateTracker.Models;
using Microsoft.Extensions.Logging;

namespace DataverseFieldUpdateTracker.FileIO;

/// <summary>
/// File I/O for exporting Dataverse metadata to JSON-lines text files.
/// Replaces file_operations.py – ImplementationDefinitionFileOperations class.
///
/// Format: one JSON object per line (JSON Lines / NDJSON).
/// OData '@' key filtering from Python is eliminated because strongly-typed
/// model objects carry no raw OData metadata.
/// </summary>
public sealed class FileOperations : IFileOperations
{
    private readonly ILogger<FileOperations> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented           = false,
        PropertyNamingPolicy    = JsonNamingPolicy.CamelCase,
    };

    public FileOperations(ILogger<FileOperations> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task WriteWorkflowFileAsync(
        IReadOnlyList<WorkflowRecord> workflows,
        string path = "./wf.txt",
        CancellationToken ct = default)
    {
        if (workflows.Count == 0)
        {
            _logger.LogWarning("No workflows to write to file");
            return;
        }

        await using var writer = new StreamWriter(path, append: false,
            encoding: System.Text.Encoding.UTF8);

        int written = 0;
        foreach (var wf in workflows)
        {
            var line = JsonSerializer.Serialize(wf, JsonOpts);
            await writer.WriteLineAsync(line.AsMemory(), ct);
            written++;
        }

        _logger.LogInformation("Wrote {Count} workflow(s) to {Path}", written, path);
    }

    /// <inheritdoc/>
    public async Task WriteWebResourceFileAsync(
        IReadOnlyList<WebResourceRecord> webResources,
        string path = "./webre.txt",
        CancellationToken ct = default)
    {
        if (webResources.Count == 0)
        {
            _logger.LogWarning("No web resources to write to file");
            return;
        }

        await using var writer = new StreamWriter(path, append: false,
            encoding: System.Text.Encoding.UTF8);

        int written = 0;
        foreach (var wr in webResources)
        {
            var line = JsonSerializer.Serialize(wr, JsonOpts);
            await writer.WriteLineAsync(line.AsMemory(), ct);
            written++;
        }

        _logger.LogInformation("Wrote {Count} web resource(s) to {Path}", written, path);
    }
}
