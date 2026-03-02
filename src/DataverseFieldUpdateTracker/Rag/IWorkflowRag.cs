namespace DataverseFieldUpdateTracker.Rag;

public interface IWorkflowRag
{
    /// <summary>
    /// Loads and indexes the workflow file. Must be called before any query method.
    /// Replaces DataverseWorkflowRAG.__init__() in workflow_rag.py.
    /// </summary>
    Task InitialiseAsync(string workflowFilePath = "./wf.txt", CancellationToken ct = default);

    /// <summary>
    /// Finds all workflows (business rules + classic) that SET/modify a specific field.
    /// Replaces find_set_value_workflows() in workflow_rag.py.
    /// </summary>
    Task<string> FindSetValueWorkflowsAsync(string fieldName, CancellationToken ct = default);

    /// <summary>
    /// Finds workflows of a specific category that modify a field.
    /// Replaces find_workflows_by_type() in workflow_rag.py.
    /// </summary>
    Task<string> FindWorkflowsByTypeAsync(string fieldName, int category, CancellationToken ct = default);

    /// <summary>
    /// Identifies all field updates across all loaded workflows.
    /// Replaces analyze_field_updates() in workflow_rag.py.
    /// </summary>
    Task<string> AnalyzeFieldUpdatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Analyses business rule actions and impacts.
    /// Replaces analyze_business_rules() in workflow_rag.py.
    /// </summary>
    Task<string> AnalyzeBusinessRulesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets details about a specific workflow by name.
    /// Replaces get_workflow_by_name() in workflow_rag.py.
    /// </summary>
    Task<string> GetWorkflowByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// General natural-language query against the indexed workflow documents.
    /// Replaces query() in workflow_rag.py.
    /// </summary>
    Task<string> QueryAsync(string question, CancellationToken ct = default);

    /// <summary>
    /// Rebuilds the index from a (potentially updated) workflow file.
    /// Replaces refresh_index() in workflow_rag.py.
    /// </summary>
    Task RefreshIndexAsync(string? workflowFilePath = null, CancellationToken ct = default);
}
