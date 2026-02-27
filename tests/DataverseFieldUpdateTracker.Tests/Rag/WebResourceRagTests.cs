using DataverseFieldUpdateTracker.FileIO;
using DataverseFieldUpdateTracker.Models;
using DataverseFieldUpdateTracker.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using Moq;
using Xunit;

namespace DataverseFieldUpdateTracker.Tests.Rag;

public sealed class WebResourceRagTests : IDisposable
{
    private readonly Mock<IChatCompletionService> _chatMock;
    private readonly Mock<ITextEmbeddingGenerationService> _embeddingMock;
    private readonly WebResourceRag _sut;
    private readonly string _tempFile;

    public WebResourceRagTests()
    {
        _chatMock      = new Mock<IChatCompletionService>();
        _embeddingMock = new Mock<ITextEmbeddingGenerationService>();

        _embeddingMock
            .Setup(e => e.GenerateEmbeddingsAsync(
                It.IsAny<IList<string>>(),
                It.IsAny<Kernel?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IList<string> texts, Kernel? k, CancellationToken ct) =>
                (IList<ReadOnlyMemory<float>>)texts
                    .Select(_ => new ReadOnlyMemory<float>(new float[8]))
                    .ToList());

        _sut      = new WebResourceRag(
            _chatMock.Object,
            _embeddingMock.Object,
            NullLogger<WebResourceRag>.Instance);

        _tempFile = Path.GetTempFileName();
    }

    private async Task InitialiseWithWebResourcesAsync(IEnumerable<WebResourceRecord> resources)
    {
        var fileOps = new FileOperations(NullLogger<FileOperations>.Instance);
        await fileOps.WriteWebResourceFileAsync(resources.ToList(), _tempFile);
        await _sut.InitialiseAsync(_tempFile);
    }

    // ── InitialiseAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task InitialiseAsync_FileNotFound_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.InitialiseAsync("/no/such/file.txt"));
    }

    [Fact]
    public async Task InitialiseAsync_EmptyFile_CompletesWithoutError()
    {
        // Web resource RAG is more lenient – empty file creates empty index
        File.WriteAllText(_tempFile, string.Empty);
        await _sut.InitialiseAsync(_tempFile); // should not throw
    }

    // ── FindSetValueWebResourcesAsync ─────────────────────────────────────────

    [Fact]
    public async Task FindSetValueWebResourcesAsync_MatchingField_CallsLlmAndReturnsResult()
    {
        var js = "formContext.getAttribute('emailaddress1').setValue(email);";
        await InitialiseWithWebResourcesAsync([
            new WebResourceRecord { Name = "account_form.js", Id = "r-1", DecodedContent = js }
        ]);

        _chatMock
            .Setup(c => c.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(),
                It.IsAny<PromptExecutionSettings?>(),
                It.IsAny<Kernel?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, "Name: account_form.js, ID: r-1")]);

        var result = await _sut.FindSetValueWebResourcesAsync("emailaddress1");

        Assert.Equal("Name: account_form.js, ID: r-1", result);
        _chatMock.Verify(c => c.GetChatMessageContentsAsync(
            It.IsAny<ChatHistory>(),
            It.IsAny<PromptExecutionSettings?>(),
            It.IsAny<Kernel?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FindSetValueWebResourcesAsync_NoMatchingField_ReturnsNoWebresourcesFound()
    {
        var js = "formContext.getAttribute('name').setValue('test');";
        await InitialiseWithWebResourcesAsync([
            new WebResourceRecord { Name = "test.js", Id = "r-2", DecodedContent = js }
        ]);

        var result = await _sut.FindSetValueWebResourcesAsync("emailaddress1");

        // No LLM call should be made
        _chatMock.Verify(c => c.GetChatMessageContentsAsync(
            It.IsAny<ChatHistory>(),
            It.IsAny<PromptExecutionSettings?>(),
            It.IsAny<Kernel?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal("No webresources found", result);
    }

    [Fact]
    public async Task FindSetValueWebResourcesAsync_CaseSensitive_NoMatchForDifferentCase()
    {
        // Python original was case-sensitive; C# preserves this
        var js = "formContext.getAttribute('Name').setValue('test');";
        await InitialiseWithWebResourcesAsync([
            new WebResourceRecord { Name = "test.js", Id = "r-3", DecodedContent = js }
        ]);

        // Search for lowercase "name" – should NOT match "Name"
        var result = await _sut.FindSetValueWebResourcesAsync("name");
        Assert.Equal("No webresources found", result);
    }

    [Fact]
    public async Task FindSetValueWebResourcesAsync_EmptyIndex_ReturnsNoWebresourcesFound()
    {
        File.WriteAllText(_tempFile, string.Empty);
        await _sut.InitialiseAsync(_tempFile);

        var result = await _sut.FindSetValueWebResourcesAsync("anyfield");
        Assert.Equal("No webresources found", result);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}
