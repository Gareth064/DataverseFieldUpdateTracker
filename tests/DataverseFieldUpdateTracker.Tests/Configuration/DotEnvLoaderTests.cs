using DataverseFieldUpdateTracker.Configuration;
using Xunit;

namespace DataverseFieldUpdateTracker.Tests.Configuration;

public sealed class DotEnvLoaderTests : IDisposable
{
    private readonly string _tempFile;
    private readonly List<string> _keysToCleanup = [];

    public DotEnvLoaderTests()
    {
        _tempFile = Path.GetTempFileName();
    }

    [Fact]
    public void Load_ParsesKeyValuePairs()
    {
        File.WriteAllText(_tempFile,
            "TEST_KEY_A=hello\n" +
            "TEST_KEY_B=world\n");

        _keysToCleanup.AddRange(["TEST_KEY_A", "TEST_KEY_B"]);
        Environment.SetEnvironmentVariable("TEST_KEY_A", null);
        Environment.SetEnvironmentVariable("TEST_KEY_B", null);

        DotEnvLoader.Load(_tempFile);

        Assert.Equal("hello", Environment.GetEnvironmentVariable("TEST_KEY_A"));
        Assert.Equal("world", Environment.GetEnvironmentVariable("TEST_KEY_B"));
    }

    [Fact]
    public void Load_StripsDoubleQuotes()
    {
        File.WriteAllText(_tempFile, "TEST_QUOTED=\"my value\"\n");
        _keysToCleanup.Add("TEST_QUOTED");
        Environment.SetEnvironmentVariable("TEST_QUOTED", null);

        DotEnvLoader.Load(_tempFile);

        Assert.Equal("my value", Environment.GetEnvironmentVariable("TEST_QUOTED"));
    }

    [Fact]
    public void Load_IgnoresCommentLines()
    {
        File.WriteAllText(_tempFile,
            "# This is a comment\n" +
            "TEST_REAL=value\n");

        _keysToCleanup.Add("TEST_REAL");
        Environment.SetEnvironmentVariable("TEST_REAL", null);

        DotEnvLoader.Load(_tempFile);

        Assert.Equal("value", Environment.GetEnvironmentVariable("TEST_REAL"));
    }

    [Fact]
    public void Load_DoesNotOverwriteExistingEnvVar()
    {
        Environment.SetEnvironmentVariable("TEST_EXISTING", "original");
        _keysToCleanup.Add("TEST_EXISTING");
        File.WriteAllText(_tempFile, "TEST_EXISTING=override\n");

        DotEnvLoader.Load(_tempFile);

        Assert.Equal("original", Environment.GetEnvironmentVariable("TEST_EXISTING"));
    }

    [Fact]
    public void Load_SilentlyIgnoresMissingFile()
    {
        // Should not throw
        DotEnvLoader.Load("nonexistent_file_that_does_not_exist.env");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);

        foreach (var key in _keysToCleanup)
            Environment.SetEnvironmentVariable(key, null);
    }
}
