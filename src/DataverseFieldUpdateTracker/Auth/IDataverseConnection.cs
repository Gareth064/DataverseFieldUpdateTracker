using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseFieldUpdateTracker.Auth;

public interface IDataverseConnection : IDisposable
{
    /// <summary>Authenticated ServiceClient for SDK-based Dataverse operations.</summary>
    ServiceClient ServiceClient { get; }

    /// <summary>The Dataverse environment URL (e.g. https://org.crm.dynamics.com/).</summary>
    string EnvironmentUrl { get; }

    /// <summary>
    /// Acquires a bearer token for direct HTTP requests to the Dataverse Web API.
    /// Azure.Identity handles caching and refresh automatically.
    /// </summary>
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}
