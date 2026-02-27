namespace DataverseFieldUpdateTracker.Models;

/// <summary>
/// Represents a reference from a form's FormXML to a web resource.
/// Produced by get_dependencylist_for_form() in dataverse_operations.py.
/// </summary>
public sealed class WebResourceReference
{
    public string FormId { get; init; } = string.Empty;
    public string WebResourceName { get; init; } = string.Empty;
}
