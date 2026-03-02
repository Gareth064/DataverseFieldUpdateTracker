using DataverseFieldUpdateTracker.Models;

namespace DataverseFieldUpdateTracker.Dataverse;

public interface IDataverseOperations
{
    /// <summary>
    /// Retrieves the MetadataId (GUID) of a specific entity attribute.
    /// Replaces get_attibuteid() in dataverse_operations.py.
    /// </summary>
    Task<string> GetAttributeIdAsync(
        string entityName, string attributeName, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all dependencies for a specific attribute using RetrieveDependenciesForDelete.
    /// Replaces get_dependencylist_for_attribute() in dataverse_operations.py.
    /// </summary>
    Task<List<DependencyItem>> GetDependencyListAsync(
        string attributeId, CancellationToken ct = default);

    /// <summary>
    /// Filters the dependency list to activated workflows and business rules.
    /// Replaces retrieve_only_workflowdependency() in dataverse_operations.py.
    /// </summary>
    Task<List<WorkflowRecord>> RetrieveWorkflowDependenciesAsync(
        List<DependencyItem> dependencyList, CancellationToken ct = default);

    /// <summary>
    /// Retrieves main (type=2) and mobile (type=6) form IDs for an entity.
    /// Replaces get_forms_for_entity() in dataverse_operations.py.
    /// </summary>
    Task<List<string>> GetFormsForEntityAsync(
        string entityName, CancellationToken ct = default);

    /// <summary>
    /// Parses FormXML to extract web resource references.
    /// Replaces get_dependencylist_for_form() in dataverse_operations.py.
    /// </summary>
    Task<List<WebResourceReference>> GetWebResourceReferencesFromFormsAsync(
        List<string> formIds, CancellationToken ct = default);

    /// <summary>
    /// Retrieves and base64-decodes JavaScript web resources by name.
    /// Replaces retrieve_webresources_from_dependency() in dataverse_operations.py.
    /// </summary>
    Task<List<WebResourceRecord>> RetrieveWebResourcesAsync(
        List<WebResourceReference> references, CancellationToken ct = default);
}
