namespace DataverseFieldUpdateTracker.Configuration;

public sealed class AppSettings
{
    public string TenantId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string EnvUrl { get; init; } = string.Empty;
    public string GoogleApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Validates that all required settings are present.
    /// Mirrors the Python ValueError raises in connect_to_dataverse.py.
    /// </summary>
    public void Validate()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(TenantId))     missing.Add("TenantId / tenant_id");
        if (string.IsNullOrWhiteSpace(ClientId))     missing.Add("ClientId / client_id");
        if (string.IsNullOrWhiteSpace(ClientSecret)) missing.Add("ClientSecret / client_secret");
        if (string.IsNullOrWhiteSpace(EnvUrl))       missing.Add("EnvUrl / env_url");
        if (string.IsNullOrWhiteSpace(GoogleApiKey)) missing.Add("GoogleApiKey / GOOGLE_API_KEY");

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Missing required configuration: {string.Join(", ", missing)}. " +
                "Please ensure these are set in your .env file or environment variables.");
    }
}
