using Diva.Tools.FileSystem;
using Diva.Tools.FileSystem.Readers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diva.Tools.Tests.FileSystem;

public sealed class PdfReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PdfReader _reader;

    public PdfReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"diva-pdf-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _reader = new PdfReader(NullLogger<PdfReader>.Instance);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string GetSamplePdfPath()
    {
        var bytes = Diva.Tools.Tests.Helpers.McpToolsTestFixtures.GetEmbeddedResource("sample.pdf");
        var path = Path.Combine(_tempDir, "sample.pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ExtractText_ValidPdf_ContainsExpectedText()
    {
        var path = GetSamplePdfPath();
        var opts = new PdfOptions();

        var result = _reader.ExtractText(path, opts);

        Assert.Contains("Hello PDF World", result);
    }

    [Fact]
    public void ExtractText_ValidPdf_IncludesMetadata()
    {
        var path = GetSamplePdfPath();
        var opts = new PdfOptions { IncludeMetadata = true };

        var result = _reader.ExtractText(path, opts);

        Assert.Contains("[PDF:", result);
    }

    [Fact]
    public void ExtractText_IncludePageNumbers_ShowsPageMarker()
    {
        var path = GetSamplePdfPath();
        var opts = new PdfOptions { IncludePageNumbers = true };

        var result = _reader.ExtractText(path, opts);

        Assert.Contains("--- Page 1 ---", result);
    }

    [Fact]
    public void ExtractText_MaxPagesRespected()
    {
        var path = GetSamplePdfPath();
        var opts = new PdfOptions { MaxPages = 0, IncludePageNumbers = false };

        var result = _reader.ExtractText(path, opts);

        Assert.DoesNotContain("--- Page 1 ---", result);
    }

    [Fact]
    public void ExtractText_CorruptFile_ReturnsErrorJson()
    {
        var path = Path.Combine(_tempDir, "corrupt.pdf");
        File.WriteAllText(path, "this is not a pdf");

        var result = _reader.ExtractText(path, new PdfOptions());

        Assert.Contains("PdfReadError", result);
    }

    [Fact]
    public void ExtractText_NotFound_ReturnsErrorJson()
    {
        var result = _reader.ExtractText(
            Path.Combine(_tempDir, "missing.pdf"), new PdfOptions());

        Assert.Contains("PdfReadError", result);
    }
}
