using Azure.Core;
using Azure.Identity;
using DataverseFieldUpdateTracker.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseFieldUpdateTracker.Auth;

/// <summary>
/// Handles Azure AD authentication and ServiceClient initialisation.
/// Replaces connect_to_dataverse.py – ConnectToDataverse class.
/// </summary>
public sealed class DataverseConnection : IDataverseConnection
{
    private readonly ClientSecretCredential _credential;
    private readonly ILogger<DataverseConnection> _logger;
    private bool _disposed;

    public ServiceClient ServiceClient { get; }
    public string EnvironmentUrl { get; }

    public DataverseConnection(AppSettings settings, ILogger<DataverseConnection> logger)
    {
        _logger = logger;
        settings.Validate();

        // Normalise URL: ensure it ends with a trailing slash
        EnvironmentUrl = settings.EnvUrl.TrimEnd('/') + '/';

        _credential = new ClientSecretCredential(
            settings.TenantId,
            settings.ClientId,
            settings.ClientSecret);

        var connectionString =
            $"AuthType=ClientSecret;" +
            $"Url={EnvironmentUrl};" +
            $"ClientId={settings.ClientId};" +
            $"ClientSecret={settings.ClientSecret};" +
            $"TenantId={settings.TenantId};" +
            $"LoginPrompt=Never";

        ServiceClient = new ServiceClient(connectionString);

        if (!ServiceClient.IsReady)
            throw new InvalidOperationException(
                $"Dataverse ServiceClient failed to connect. " +
                $"Last error: {ServiceClient.LastError ?? "unknown"}. " +
                "Please verify your Azure credentials and environment URL.");

        _logger.LogInformation("Connected to Dataverse: {Url}", EnvironmentUrl);
    }

    /// <summary>
    /// Returns a fresh bearer token. Azure.Identity caches and refreshes automatically,
    /// resolving the Python limitation of tokens expiring after 1 hour.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var context = new TokenRequestContext(
            new[] { $"{EnvironmentUrl.TrimEnd('/')}.default" });

        var result = await _credential.GetTokenAsync(context, ct);
        return result.Token;
    }

    public void Dispose()
    {
        if (_disposed) return;
        ServiceClient?.Dispose();
        _disposed = true;
    }
}
