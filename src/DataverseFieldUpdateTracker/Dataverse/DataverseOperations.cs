using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataverseFieldUpdateTracker.Auth;
using DataverseFieldUpdateTracker.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseFieldUpdateTracker.Dataverse;

/// <summary>
/// Core Dataverse operations using the PowerPlatform SDK (for table queries)
/// and HttpClient (for Metadata API and custom OData functions).
/// Replaces dataverse_operations.py – DataverseOperations class.
/// </summary>
public sealed class DataverseOperations : IDataverseOperations
{
    private const string ApiVersion = "9.2";

    private readonly IDataverseConnection _connection;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DataverseOperations> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // FormXML web resource reference patterns (ported from dataverse_operations.py)
    private static readonly Regex LibraryPattern =
        new(@"<Library\s+name=""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WebResourcePattern =
        new(@"<WebResource[^>]+id=""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SrcPattern =
        new(@"src=""([^""]+(\.js|\.css|\.html))""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public DataverseOperations(
        IDataverseConnection connection,
        IHttpClientFactory httpClientFactory,
        ILogger<DataverseOperations> logger)
    {
        _connection = connection;
        _httpClient = httpClientFactory.CreateClient(nameof(DataverseOperations));
        _logger = logger;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SetAuthHeaderAsync(CancellationToken ct)
    {
        var token = await _connection.GetAccessTokenAsync(ct);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("OData-Version", "4.0");
    }

    private string BaseUrl => $"{_connection.EnvironmentUrl}api/data/v{ApiVersion}/";

    // ── GetAttributeIdAsync ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> GetAttributeIdAsync(
        string entityName, string attributeName, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Retrieving attribute ID for {Entity}.{Attribute}", entityName, attributeName);

        await SetAuthHeaderAsync(ct);

        var url = $"{BaseUrl}EntityDefinitions(LogicalName='{entityName}')" +
                  $"/Attributes?$filter=LogicalName eq '{attributeName}'";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve attribute ID for '{entityName}.{attributeName}': {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Dataverse API returned {(int)response.StatusCode} for " +
                $"attribute '{entityName}.{attributeName}'.");

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("value", out var valueArray) ||
            valueArray.GetArrayLength() == 0)
            throw new InvalidOperationException(
                $"Attribute '{attributeName}' not found for entity '{entityName}'. " +
                "Please verify the entity and attribute names.");

        var metadataId = valueArray[0].GetProperty("MetadataId").GetString();

        if (string.IsNullOrWhiteSpace(metadataId))
            throw new InvalidOperationException(
                $"MetadataId is null/empty for attribute '{attributeName}' on entity '{entityName}'.");

        _logger.LogInformation("Found attribute ID: {Id}", metadataId);
        return metadataId;
    }

    // ── GetDependencyListAsync ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<DependencyItem>> GetDependencyListAsync(
        string attributeId, CancellationToken ct = default)
    {
        _logger.LogInformation("Retrieving dependencies for attribute {Id}", attributeId);

        await SetAuthHeaderAsync(ct);

        var url = $"{BaseUrl}RetrieveDependenciesForDelete" +
                  $"(ObjectId={attributeId},ComponentType=2)";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve dependencies for attribute '{attributeId}': {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Dataverse API returned {(int)response.StatusCode} " +
                $"retrieving dependencies for attribute '{attributeId}'.");

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("value", out var valueElement))
            throw new InvalidOperationException(
                $"Unexpected response format for dependencies of attribute '{attributeId}'. " +
                "Expected 'value' key in response.");

        var items = JsonSerializer.Deserialize<List<DependencyItem>>(
            valueElement.GetRawText(), _jsonOptions) ?? [];

        _logger.LogInformation("Found {Count} dependencies", items.Count);
        return items;
    }

    // ── RetrieveWorkflowDependenciesAsync ─────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<WorkflowRecord>> RetrieveWorkflowDependenciesAsync(
        List<DependencyItem> dependencyList, CancellationToken ct = default)
    {
        // Filter: componenttype==29 (workflow), dependencytype==2
        var workflowIds = dependencyList
            .Where(d => d.DependentComponentType == 29 && d.DependencyType == 2)
            .Select(d => d.DependentComponentObjectId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        _logger.LogInformation(
            "Processing {Count} workflow dependency candidates", workflowIds.Count);

        var results = new List<WorkflowRecord>();

        foreach (var idStr in workflowIds)
        {
            if (!Guid.TryParse(idStr, out var id))
            {
                _logger.LogWarning("Skipping invalid workflow GUID: {Id}", idStr);
                continue;
            }

            try
            {
                var entity = _connection.ServiceClient.Retrieve(
                    "workflow",
                    id,
                    new ColumnSet("name", "category", "xaml", "statecode"));

                var stateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? -1;
                var category  = entity.GetAttributeValue<OptionSetValue>("category")?.Value ?? -1;

                // Only activated (statecode=1) classic workflows (0) or business rules (2)
                if (stateCode == 1 && (category == 0 || category == 2))
                {
                    results.Add(new WorkflowRecord
                    {
                        Name       = entity.GetAttributeValue<string>("name") ?? string.Empty,
                        WorkflowId = entity.Id.ToString(),
                        Category   = category,
                        Xaml       = entity.GetAttributeValue<string>("xaml") ?? string.Empty,
                        StateCode  = stateCode,
                    });

                    _logger.LogInformation(
                        "Added workflow: {Name} (category={Cat})",
                        entity.GetAttributeValue<string>("name"), category);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to retrieve workflow {Id}: {Message}", idStr, ex.Message);
                // Graceful degradation – continue with remaining workflows
            }
        }

        _logger.LogInformation("Returning {Count} qualifying workflows", results.Count);
        return results;
    }

    // ── GetFormsForEntityAsync ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<string>> GetFormsForEntityAsync(
        string entityName, CancellationToken ct = default)
    {
        _logger.LogInformation("Retrieving forms for entity '{Entity}'", entityName);

        var query = new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid"),
        };

        query.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, entityName);

        var typeFilter = new FilterExpression(LogicalOperator.Or);
        typeFilter.AddCondition("type", ConditionOperator.Equal, 2); // Main form
        typeFilter.AddCondition("type", ConditionOperator.Equal, 6); // Mobile form
        query.Criteria.AddFilter(typeFilter);

        query.PageInfo = new PagingInfo { Count = 250, PageNumber = 1 };

        var formIds = new List<string>();

        try
        {
            EntityCollection results;
            do
            {
                results = _connection.ServiceClient.RetrieveMultiple(query);
                foreach (var entity in results.Entities)
                {
                    var formId = entity.GetAttributeValue<Guid>("formid");
                    if (formId != Guid.Empty)
                        formIds.Add(formId.ToString());
                }

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = results.PagingCookie;
            }
            while (results.MoreRecords);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve forms for entity '{entityName}': {ex.Message}", ex);
        }

        _logger.LogInformation("Found {Count} forms for '{Entity}'", formIds.Count, entityName);
        return formIds;
    }

    // ── GetWebResourceReferencesFromFormsAsync ────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<WebResourceReference>> GetWebResourceReferencesFromFormsAsync(
        List<string> formIds, CancellationToken ct = default)
    {
        if (formIds.Count == 0)
            return [];

        var references = new List<WebResourceReference>();

        foreach (var formIdStr in formIds)
        {
            if (!Guid.TryParse(formIdStr, out var formId))
            {
                _logger.LogWarning("Skipping invalid form GUID: {Id}", formIdStr);
                continue;
            }

            try
            {
                var entity = _connection.ServiceClient.Retrieve(
                    "systemform",
                    formId,
                    new ColumnSet("formxml", "name"));

                var formXml = entity.GetAttributeValue<string>("formxml");
                if (string.IsNullOrWhiteSpace(formXml))
                    continue;

                var allRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match m in LibraryPattern.Matches(formXml))
                    allRefs.Add(m.Groups[1].Value);

                foreach (Match m in WebResourcePattern.Matches(formXml))
                    allRefs.Add(m.Groups[1].Value);

                foreach (Match m in SrcPattern.Matches(formXml))
                    allRefs.Add(m.Groups[1].Value);

                foreach (var name in allRefs)
                    references.Add(new WebResourceReference
                    {
                        FormId          = formIdStr,
                        WebResourceName = name,
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Error processing form {Id}: {Message}", formIdStr, ex.Message);
            }
        }

        _logger.LogInformation(
            "Found {Count} web resource references across {Forms} form(s)",
            references.Count, formIds.Count);

        return references;
    }

    // ── RetrieveWebResourcesAsync ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<List<WebResourceRecord>> RetrieveWebResourcesAsync(
        List<WebResourceReference> references, CancellationToken ct = default)
    {
        if (references.Count == 0)
            return [];

        var results = new List<WebResourceRecord>();
        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references)
        {
            var name = reference.WebResourceName;
            if (string.IsNullOrWhiteSpace(name) || !processedNames.Add(name))
                continue;

            try
            {
                var query = new QueryExpression("webresource")
                {
                    ColumnSet = new ColumnSet("name", "webresourcetype", "content", "webresourceid"),
                    TopCount  = 1,
                };
                query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
                // Only JavaScript (webresourcetype = 3)
                query.Criteria.AddCondition("webresourcetype", ConditionOperator.Equal, 3);

                var collection = _connection.ServiceClient.RetrieveMultiple(query);

                if (collection.Entities.Count == 0) continue;

                var entity = collection.Entities[0];
                var content = entity.GetAttributeValue<string>("content");

                var decoded = string.Empty;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    try
                    {
                        decoded = System.Text.Encoding.UTF8.GetString(
                            Convert.FromBase64String(content));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            "Error decoding content for '{Name}': {Message}", name, ex.Message);
                        decoded = $"Error decoding content: {ex.Message}";
                    }
                }

                results.Add(new WebResourceRecord
                {
                    Name          = entity.GetAttributeValue<string>("name") ?? name,
                    Id            = entity.GetAttributeValue<Guid>("webresourceid").ToString(),
                    DecodedContent = decoded,
                });

                _logger.LogInformation("Retrieved web resource: {Name}", name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Error processing web resource '{Name}': {Message}", name, ex.Message);
            }
        }

        _logger.LogInformation("Returning {Count} web resources", results.Count);
        return results;
    }
}
