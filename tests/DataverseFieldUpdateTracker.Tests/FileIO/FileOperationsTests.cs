using System.Text.Json;
using DataverseFieldUpdateTracker.FileIO;
using DataverseFieldUpdateTracker.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataverseFieldUpdateTracker.Tests.FileIO;

public sealed class FileOperationsTests : IDisposable
{
    private readonly FileOperations _sut;
    private readonly string _wfTempFile;
    private readonly string _wrTempFile;

    public FileOperationsTests()
    {
        _sut       = new FileOperations(NullLogger<FileOperations>.Instance);
        _wfTempFile = Path.GetTempFileName();
        _wrTempFile = Path.GetTempFileName();
    }

    // ── WriteWorkflowFileAsync ────────────────────────────────────────────────

    [Fact]
    public async Task WriteWorkflowFileAsync_WritesOneJsonLinePerWorkflow()
    {
        var workflows = new List<WorkflowRecord>
        {
            new() { Name = "Rule A", WorkflowId = "id1", Category = 2, Xaml = "<xml/>", StateCode = 1 },
            new() { Name = "WF B",   WorkflowId = "id2", Category = 0, Xaml = "<xml/>", StateCode = 1 },
        };

        await _sut.WriteWorkflowFileAsync(workflows, _wfTempFile);

        var lines = await File.ReadAllLinesAsync(_wfTempFile);
        // Filter blank lines
        var nonEmpty = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        Assert.Equal(2, nonEmpty.Length);
    }

    [Fact]
    public async Task WriteWorkflowFileAsync_EachLineIsValidJson()
    {
        var workflows = new List<WorkflowRecord>
        {
            new() { Name = "My BR", WorkflowId = "abc", Category = 2, Xaml = "xaml", StateCode = 1 },
        };

        await _sut.WriteWorkflowFileAsync(workflows, _wfTempFile);

        var line = (await File.ReadAllLinesAsync(_wfTempFile))
            .First(l => !string.IsNullOrWhiteSpace(l));

        using var doc = JsonDocument.Parse(line);
        Assert.Equal("My BR", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("abc",   doc.RootElement.GetProperty("workflowId").GetString());
        Assert.Equal(2,       doc.RootElement.GetProperty("category").GetInt32());
    }

    [Fact]
    public async Task WriteWorkflowFileAsync_EmptyList_WritesNothing()
    {
        await _sut.WriteWorkflowFileAsync([], _wfTempFile);

        // File should exist but have no meaningful content
        var content = await File.ReadAllTextAsync(_wfTempFile);
        Assert.True(string.IsNullOrWhiteSpace(content));
    }

    [Fact]
    public async Task WriteWorkflowFileAsync_OverwritesExistingFile()
    {
        await File.WriteAllTextAsync(_wfTempFile, "old data");

        var workflows = new List<WorkflowRecord>
        {
            new() { Name = "New Rule", WorkflowId = "x", Category = 2, Xaml = "", StateCode = 1 },
        };

        await _sut.WriteWorkflowFileAsync(workflows, _wfTempFile);

        var content = await File.ReadAllTextAsync(_wfTempFile);
        Assert.DoesNotContain("old data", content);
        Assert.Contains("New Rule", content);
    }

    // ── WriteWebResourceFileAsync ─────────────────────────────────────────────

    [Fact]
    public async Task WriteWebResourceFileAsync_WritesOneJsonLinePerResource()
    {
        var resources = new List<WebResourceRecord>
        {
            new() { Name = "new_script.js", Id = "r1", DecodedContent = "function(){}" },
            new() { Name = "another.js",    Id = "r2", DecodedContent = "var x=1;"    },
        };

        await _sut.WriteWebResourceFileAsync(resources, _wrTempFile);

        var lines = (await File.ReadAllLinesAsync(_wrTempFile))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task WriteWebResourceFileAsync_EachLineIsValidJson()
    {
        var resources = new List<WebResourceRecord>
        {
            new() { Name = "test.js", Id = "guid-1", DecodedContent = "var a=1;" },
        };

        await _sut.WriteWebResourceFileAsync(resources, _wrTempFile);

        var line = (await File.ReadAllLinesAsync(_wrTempFile))
            .First(l => !string.IsNullOrWhiteSpace(l));

        using var doc = JsonDocument.Parse(line);
        Assert.Equal("test.js",  doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("guid-1",   doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("var a=1;", doc.RootElement.GetProperty("decodedContent").GetString());
    }

    public void Dispose()
    {
        if (File.Exists(_wfTempFile)) File.Delete(_wfTempFile);
        if (File.Exists(_wrTempFile)) File.Delete(_wrTempFile);
    }
}
