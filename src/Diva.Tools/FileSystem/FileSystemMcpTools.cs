using System.ComponentModel;
using System.Text.Json;
using Diva.Tools.Core;
using Diva.Tools.FileSystem.Abstractions;
using Diva.Tools.FileSystem.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Diva.Tools.FileSystem;

[McpServerToolType]
public sealed class FileSystemMcpTools(
    IHttpContextAccessor http,
    IFileSystemPathGuard guard,
    IToolFilter filter,
    IPdfReader pdfReader,
    IImageReader imageReader,
    IOptions<FileSystemOptions> opts,
    ILogger<FileSystemMcpTools> logger) : IDivaMcpToolType
{
    private readonly FileSystemOptions _opts = opts.Value;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // ── Helpers ────────────────────────────────────────────────────────────

    private string DisabledError(string tool) =>
        JsonSerializer.Serialize(new { error = "ToolDisabled", message = $"Tool '{tool}' is not enabled in this configuration." }, _json);

    private string AccessError(string message) =>
        JsonSerializer.Serialize(new { error = "AccessDenied", message }, _json);

    private string IoError(string message) =>
        JsonSerializer.Serialize(new { error = "IoError", message }, _json);

    private string WriteDisabledError() =>
        JsonSerializer.Serialize(new { error = "WriteDisabled", message = "Write operations are disabled (AllowWrites=false)." }, _json);

    private void LogDebug(string tool, McpServerContext ctx, long startMs) =>
        logger.LogDebug("{Tool} tenant={TenantId} elapsedMs={Ms}", tool, ctx.TenantId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs);

    // ── Read tools ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "read_file")]
    [Description("Read a text file (or dispatch to read_pdf for .pdf). Returns file content as UTF-8 text.")]
    public string ReadFile([Description("Absolute path to the file")] string path)
    {
        if (!filter.IsEnabled("read_file")) return DisabledError("read_file");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);

            if (!_opts.TextEnabled)
                return AccessError("Text file reading is disabled (TextEnabled=false).");

            var ext = Path.GetExtension(canonical).ToLowerInvariant();
            if (ext == ".pdf")
            {
                if (!_opts.PdfEnabled) return AccessError("PDF reading is disabled (PdfEnabled=false).");
                return pdfReader.ExtractText(canonical, _opts.Pdf);
            }

            var info = new FileInfo(canonical);
            if (!info.Exists)
                return IoError($"File not found: {canonical}");

            if (info.Length > _opts.MaxReadFileSizeBytes)
                return IoError($"File too large: {info.Length} bytes (max {_opts.MaxReadFileSizeBytes}).");

            var content = File.ReadAllText(canonical, System.Text.Encoding.UTF8);
            LogDebug("read_file", ctx, t);
            return content;
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("read_file access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("read_file bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("read_file io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "read_pdf")]
    [Description("Extract text and metadata from a PDF file. Returns page-delimited text.")]
    public string ReadPdf([Description("Absolute path to the PDF file")] string path)
    {
        if (!filter.IsEnabled("read_pdf")) return DisabledError("read_pdf");
        if (!_opts.PdfEnabled) return AccessError("PDF reading is disabled (PdfEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            var result = pdfReader.ExtractText(canonical, _opts.Pdf);
            LogDebug("read_pdf", ctx, t);
            return result;
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("read_pdf access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("read_pdf bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("read_pdf io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "get_image_info")]
    [Description("Analyze an image and return metadata, quality metrics (blur, exposure), and EXIF data. No base64 content returned.")]
    public string GetImageInfo([Description("Absolute path to the image file")] string path)
    {
        if (!filter.IsEnabled("get_image_info")) return DisabledError("get_image_info");
        if (!_opts.ImagesEnabled) return AccessError("Image reading is disabled (ImagesEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            var infoOpts = new ImageOptions
            {
                ExtractExif = _opts.Image.ExtractExif,
                ComputeQualityMetrics = _opts.Image.ComputeQualityMetrics,
                ReturnBase64 = false,
                ReturnThumbnail = false,
                BlurThreshold = _opts.Image.BlurThreshold,
                ExposureUnderThreshold = _opts.Image.ExposureUnderThreshold,
                ExposureOverThreshold = _opts.Image.ExposureOverThreshold
            };
            var result = imageReader.Analyze(canonical, infoOpts);
            LogDebug("get_image_info", ctx, t);
            return JsonSerializer.Serialize(result, _json);
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("get_image_info access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("get_image_info bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("get_image_info io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "read_image")]
    [Description("Analyze an image and return metadata, quality metrics, EXIF, and optionally base64 content and thumbnail.")]
    public string ReadImage([Description("Absolute path to the image file")] string path)
    {
        if (!filter.IsEnabled("read_image")) return DisabledError("read_image");
        if (!_opts.ImagesEnabled) return AccessError("Image reading is disabled (ImagesEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            var result = imageReader.Analyze(canonical, _opts.Image);
            LogDebug("read_image", ctx, t);
            return JsonSerializer.Serialize(result, _json);
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("read_image access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("read_image bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("read_image io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "list_directory")]
    [Description("List files and directories in a path. Supports optional search pattern.")]
    public string ListDirectory(
        [Description("Absolute path to the directory")] string path,
        [Description("Optional glob pattern, e.g. *.txt")] string? pattern = null)
    {
        if (!filter.IsEnabled("list_directory")) return DisabledError("list_directory");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            var dir = new DirectoryInfo(canonical);
            if (!dir.Exists) return IoError($"Directory not found: {canonical}");

            var searchPattern = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
            var entries = dir.EnumerateFileSystemInfos(searchPattern, SearchOption.TopDirectoryOnly)
                .Take(_opts.MaxDirectoryListEntries)
                .Select(fsi =>
                {
                    var type = fsi is DirectoryInfo ? "directory"
                        : fsi.LinkTarget is not null ? "symlink"
                        : "file";
                    long? size = fsi is FileInfo fi ? fi.Length : null;
                    return new DirectoryEntry(
                        Name: fsi.Name,
                        FullPath: fsi.FullName,
                        Type: type,
                        SizeBytes: size,
                        Modified: fsi.LastWriteTimeUtc.ToString("O"),
                        IsReadOnly: fsi.Attributes.HasFlag(FileAttributes.ReadOnly));
                })
                .ToList();

            LogDebug("list_directory", ctx, t);
            return JsonSerializer.Serialize(entries, _json);
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("list_directory access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("list_directory bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("list_directory io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "get_file_info")]
    [Description("Get metadata about a file or directory: size, timestamps, type, symlink status.")]
    public string GetFileInfo([Description("Absolute path to the file or directory")] string path)
    {
        if (!filter.IsEnabled("get_file_info")) return DisabledError("get_file_info");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            FileInfoResult result;

            if (Directory.Exists(canonical))
            {
                var d = new DirectoryInfo(canonical);
                result = new FileInfoResult(
                    Name: d.Name,
                    FullPath: d.FullName,
                    SizeBytes: 0,
                    Created: d.CreationTimeUtc.ToString("O"),
                    Modified: d.LastWriteTimeUtc.ToString("O"),
                    IsDirectory: true,
                    IsReadOnly: d.Attributes.HasFlag(FileAttributes.ReadOnly),
                    IsSymlink: d.LinkTarget is not null,
                    Extension: null);
            }
            else if (File.Exists(canonical))
            {
                var f = new FileInfo(canonical);
                result = new FileInfoResult(
                    Name: f.Name,
                    FullPath: f.FullName,
                    SizeBytes: f.Length,
                    Created: f.CreationTimeUtc.ToString("O"),
                    Modified: f.LastWriteTimeUtc.ToString("O"),
                    IsDirectory: false,
                    IsReadOnly: f.IsReadOnly,
                    IsSymlink: f.LinkTarget is not null,
                    Extension: f.Extension);
            }
            else
            {
                return IoError($"Path does not exist: {canonical}");
            }

            LogDebug("get_file_info", ctx, t);
            return JsonSerializer.Serialize(result, _json);
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("get_file_info access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("get_file_info bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("get_file_info io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "search_files")]
    [Description("Recursively search for files matching a glob pattern under a base directory.")]
    public string SearchFiles(
        [Description("Absolute base directory path")] string basePath,
        [Description("Glob pattern, e.g. *.log or **/*.json")] string pattern)
    {
        if (!filter.IsEnabled("search_files")) return DisabledError("search_files");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(basePath);
            if (!Directory.Exists(canonical)) return IoError($"Directory not found: {canonical}");

            var results = Directory.EnumerateFiles(canonical, pattern,
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        MatchCasing = MatchCasing.CaseInsensitive
                    })
                .Take(_opts.MaxSearchResults)
                .ToList();

            LogDebug("search_files", ctx, t);
            return JsonSerializer.Serialize(results, _json);
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("search_files access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("search_files bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("search_files io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "get_allowed_roots")]
    [Description("Returns the list of root paths accessible to this server (based on AllowedBasePaths config or platform drives).")]
    public string GetAllowedRoots()
    {
        if (!filter.IsEnabled("get_allowed_roots")) return DisabledError("get_allowed_roots");
        var roots = guard.GetAllowedRoots();
        return JsonSerializer.Serialize(roots, _json);
    }

    // ── Write tools ────────────────────────────────────────────────────────

    [McpServerTool(Name = "write_file")]
    [Description("Write (or overwrite) a file with text content. Requires AllowWrites=true in config.")]
    public string WriteFile(
        [Description("Absolute path to the file")] string path,
        [Description("Text content to write")] string content)
    {
        if (!filter.IsEnabled("write_file")) return DisabledError("write_file");
        if (!_opts.AllowWrites) return WriteDisabledError();
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            File.WriteAllText(canonical, content, System.Text.Encoding.UTF8);
            LogDebug("write_file", ctx, t);
            return "ok";
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("write_file access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("write_file bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("write_file io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "create_directory")]
    [Description("Create a directory (and all intermediate directories). Requires AllowWrites=true.")]
    public string CreateDirectory([Description("Absolute path for the new directory")] string path)
    {
        if (!filter.IsEnabled("create_directory")) return DisabledError("create_directory");
        if (!_opts.AllowWrites) return WriteDisabledError();
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            Directory.CreateDirectory(canonical);
            LogDebug("create_directory", ctx, t);
            return "ok";
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("create_directory access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("create_directory bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("create_directory io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "delete_file")]
    [Description("Delete a file. Requires AllowWrites=true in config.")]
    public string DeleteFile([Description("Absolute path to the file to delete")] string path)
    {
        if (!filter.IsEnabled("delete_file")) return DisabledError("delete_file");
        if (!_opts.AllowWrites) return WriteDisabledError();
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            if (!File.Exists(canonical)) return IoError($"File not found: {canonical}");
            File.Delete(canonical);
            LogDebug("delete_file", ctx, t);
            return "ok";
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("delete_file access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("delete_file bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("delete_file io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "move_item")]
    [Description("Move or rename a file or directory. Requires AllowWrites=true.")]
    public string MoveItem(
        [Description("Absolute source path")] string sourcePath,
        [Description("Absolute destination path")] string destinationPath)
    {
        if (!filter.IsEnabled("move_item")) return DisabledError("move_item");
        if (!_opts.AllowWrites) return WriteDisabledError();
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonicalSrc  = guard.Validate(sourcePath);
            var canonicalDest = guard.Validate(destinationPath);

            if (Directory.Exists(canonicalSrc))
                Directory.Move(canonicalSrc, canonicalDest);
            else if (File.Exists(canonicalSrc))
                File.Move(canonicalSrc, canonicalDest, overwrite: false);
            else
                return IoError($"Source not found: {canonicalSrc}");

            LogDebug("move_item", ctx, t);
            return "ok";
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("move_item access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("move_item bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("move_item io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }
}
