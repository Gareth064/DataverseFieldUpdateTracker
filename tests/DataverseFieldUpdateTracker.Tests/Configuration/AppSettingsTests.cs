using DataverseFieldUpdateTracker.Configuration;
using Xunit;

namespace DataverseFieldUpdateTracker.Tests.Configuration;

public sealed class AppSettingsTests
{
    [Fact]
    public void Validate_AllFieldsSet_DoesNotThrow()
    {
        var settings = new AppSettings
        {
            TenantId     = "tenant",
            ClientId     = "client",
            ClientSecret = "secret",
            EnvUrl       = "https://org.crm.dynamics.com",
            GoogleApiKey = "key",
        };

        // Should not throw
        settings.Validate();
    }

    [Fact]
    public void Validate_MissingTenantId_Throws()
    {
        var settings = new AppSettings
        {
            ClientId     = "client",
            ClientSecret = "secret",
            EnvUrl       = "https://org.crm.dynamics.com",
            GoogleApiKey = "key",
        };

        var ex = Assert.Throws<InvalidOperationException>(() => settings.Validate());
        Assert.Contains("TenantId", ex.Message);
    }

    [Fact]
    public void Validate_MultipleFieldsMissing_ListsAllInMessage()
    {
        var settings = new AppSettings(); // all empty

        var ex = Assert.Throws<InvalidOperationException>(() => settings.Validate());
        Assert.Contains("TenantId", ex.Message);
        Assert.Contains("ClientId", ex.Message);
        Assert.Contains("EnvUrl", ex.Message);
        Assert.Contains("GoogleApiKey", ex.Message);
    }

    [Fact]
    public void Validate_MissingGoogleApiKey_Throws()
    {
        var settings = new AppSettings
        {
            TenantId     = "tenant",
            ClientId     = "client",
            ClientSecret = "secret",
            EnvUrl       = "https://org.crm.dynamics.com",
            // GoogleApiKey omitted
        };

        var ex = Assert.Throws<InvalidOperationException>(() => settings.Validate());
        Assert.Contains("GoogleApiKey", ex.Message);
    }
}
