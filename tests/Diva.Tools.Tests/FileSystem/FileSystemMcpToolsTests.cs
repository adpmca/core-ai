using System.Text.Json;
using Diva.Tools.FileSystem;
using Diva.Tools.Tests.Helpers;

namespace Diva.Tools.Tests.FileSystem;

public sealed class FileSystemMcpToolsTests : IDisposable
{
    private readonly FileSystemMcpTools _tools;
    private readonly string _tempDir;

    public FileSystemMcpToolsTests()
    {
        (_tools, _tempDir) = McpToolsTestFixtures.BuildOverTempDir();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── read_file ──────────────────────────────────────────────────────────

    [Fact]
    public void ReadFile_RoundTrip()
    {
        var path = Path.Combine(_tempDir, "hello.txt");
        File.WriteAllText(path, "Hello World");

        var result = _tools.ReadFile(path);

        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void ReadFile_Oversized_ReturnsError()
    {
        var path = Path.Combine(_tempDir, "big.txt");
        File.WriteAllText(path, "x");

        var opts = new FileSystemOptions
        {
            AllowedBasePaths = [_tempDir],
            MaxReadFileSizeBytes = 0  // force size error
        };
        var (tools, _) = McpToolsTestFixtures.BuildOverTempDir(opts);
        var result = tools.ReadFile(path);

        AssertIsError(result, "IoError");
    }

    [Fact]
    public void ReadFile_NotFound_ReturnsError()
    {
        var result = _tools.ReadFile(Path.Combine(_tempDir, "missing.txt"));

        AssertIsError(result, "IoError");
    }

    [Fact]
    public void ReadFile_PathTraversal_ReturnsAccessDenied()
    {
        var traversal = Path.Combine(_tempDir, "..", "outside.txt");

        var result = _tools.ReadFile(traversal);

        AssertIsError(result, "AccessDenied");
    }

    // ── list_directory ─────────────────────────────────────────────────────

    [Fact]
    public void ListDirectory_ReturnsEntries()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "1");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "2");

        var result = _tools.ListDirectory(_tempDir);

        var entries = JsonSerializer.Deserialize<List<JsonElement>>(result);
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("file", e.GetProperty("type").GetString()));
    }

    [Fact]
    public void ListDirectory_CapsAtMaxEntries()
    {
        for (var i = 0; i < 10; i++)
            File.WriteAllText(Path.Combine(_tempDir, $"file{i}.txt"), "x");

        var opts = new FileSystemOptions
        {
            AllowedBasePaths = [_tempDir],
            MaxDirectoryListEntries = 3
        };
        var (tools, _) = McpToolsTestFixtures.BuildOverTempDir(opts);

        var result = tools.ListDirectory(_tempDir);
        var entries = JsonSerializer.Deserialize<List<JsonElement>>(result);
        Assert.NotNull(entries);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void ListDirectory_NotFound_ReturnsError()
    {
        var result = _tools.ListDirectory(Path.Combine(_tempDir, "nonexistent"));

        AssertIsError(result, "IoError");
    }

    // ── search_files ───────────────────────────────────────────────────────

    [Fact]
    public void SearchFiles_FindsMatchingFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.log"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "b.log"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "c.txt"), "x");

        var result = _tools.SearchFiles(_tempDir, "*.log");

        var files = JsonSerializer.Deserialize<List<string>>(result);
        Assert.NotNull(files);
        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.EndsWith(".log", f));
    }

    // ── get_file_info ──────────────────────────────────────────────────────

    [Fact]
    public void GetFileInfo_File_ReturnsCorrectInfo()
    {
        var path = Path.Combine(_tempDir, "info.txt");
        File.WriteAllText(path, "test content");

        var result = _tools.GetFileInfo(path);

        var doc = JsonDocument.Parse(result).RootElement;
        Assert.Equal("info.txt", doc.GetProperty("name").GetString());
        Assert.Equal(12, doc.GetProperty("sizeBytes").GetInt64());
        Assert.False(doc.GetProperty("isDirectory").GetBoolean());
        Assert.Equal(".txt", doc.GetProperty("extension").GetString());
    }

    [Fact]
    public void GetFileInfo_Directory_ReturnsCorrectInfo()
    {
        var subDir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(subDir);

        var result = _tools.GetFileInfo(subDir);

        var doc = JsonDocument.Parse(result).RootElement;
        Assert.True(doc.GetProperty("isDirectory").GetBoolean());
        Assert.Equal("subdir", doc.GetProperty("name").GetString());
    }

    // ── write/create/delete/move ───────────────────────────────────────────

    [Fact]
    public void WriteFile_WritesContent_And_ReadBack()
    {
        var path = Path.Combine(_tempDir, "written.txt");

        var writeResult = _tools.WriteFile(path, "Diva Phase 23");
        Assert.Equal("ok", writeResult);

        var readResult = _tools.ReadFile(path);
        Assert.Equal("Diva Phase 23", readResult);
    }

    [Fact]
    public void WriteFile_WriteDisabled_ReturnsError()
    {
        var opts = new FileSystemOptions
        {
            AllowedBasePaths = [_tempDir],
            AllowWrites = false
        };
        var (tools, _) = McpToolsTestFixtures.BuildOverTempDir(opts);

        var result = tools.WriteFile(Path.Combine(_tempDir, "x.txt"), "data");

        AssertIsError(result, "WriteDisabled");
    }

    [Fact]
    public void DeleteFile_Succeeds()
    {
        var path = Path.Combine(_tempDir, "to-delete.txt");
        File.WriteAllText(path, "bye");

        var result = _tools.DeleteFile(path);
        Assert.Equal("ok", result);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void MoveItem_Succeeds()
    {
        var src  = Path.Combine(_tempDir, "src.txt");
        var dest = Path.Combine(_tempDir, "dest.txt");
        File.WriteAllText(src, "move me");

        var result = _tools.MoveItem(src, dest);
        Assert.Equal("ok", result);
        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dest));
    }

    // ── disabled tool ──────────────────────────────────────────────────────

    [Fact]
    public void DisabledTool_ReturnsToolDisabledError()
    {
        var opts = new FileSystemOptions
        {
            AllowedBasePaths = [_tempDir],
            EnabledTools = ["list_directory"]  // only this tool enabled
        };
        var (tools, _) = McpToolsTestFixtures.BuildOverTempDir(opts);

        var result = tools.ReadFile(Path.Combine(_tempDir, "file.txt"));

        AssertIsError(result, "ToolDisabled");
    }

    // ── get_allowed_roots ──────────────────────────────────────────────────

    [Fact]
    public void GetAllowedRoots_ReturnsNonEmpty()
    {
        var result = _tools.GetAllowedRoots();

        var roots = JsonSerializer.Deserialize<List<string>>(result);
        Assert.NotNull(roots);
        Assert.NotEmpty(roots);
    }

    // ── helper ─────────────────────────────────────────────────────────────

    private static void AssertIsError(string json, string expectedErrorCode)
    {
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.True(doc.TryGetProperty("error", out var err),
            $"Expected 'error' field in: {json}");
        Assert.Equal(expectedErrorCode, err.GetString());
    }
}
