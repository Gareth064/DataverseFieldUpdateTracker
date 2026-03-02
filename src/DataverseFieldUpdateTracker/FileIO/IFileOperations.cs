using DataverseFieldUpdateTracker.Models;

namespace DataverseFieldUpdateTracker.FileIO;

public interface IFileOperations
{
    /// <summary>
    /// Writes workflow records to a JSON-lines file (one JSON object per line).
    /// Replaces create_workflow_file() in file_operations.py.
    /// </summary>
    Task WriteWorkflowFileAsync(
        IReadOnlyList<WorkflowRecord> workflows,
        string path = "./wf.txt",
        CancellationToken ct = default);

    /// <summary>
    /// Writes web resource records to a JSON-lines file (one JSON object per line).
    /// Replaces create_webresourceflow_file() in file_operations.py.
    /// </summary>
    Task WriteWebResourceFileAsync(
        IReadOnlyList<WebResourceRecord> webResources,
        string path = "./webre.txt",
        CancellationToken ct = default);
}
