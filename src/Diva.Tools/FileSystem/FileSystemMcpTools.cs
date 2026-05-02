using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using Diva.Tools.Core;
using Diva.Tools.FileSystem.Abstractions;
using Diva.Tools.FileSystem.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Diva.Tools.FileSystem.Readers;
using Diva.Tools.FileSystem.Writers;

namespace Diva.Tools.FileSystem;

[McpServerToolType]
public sealed class FileSystemMcpTools(
    IHttpContextAccessor http,
    IFileSystemPathGuard guard,
    IToolFilter filter,
    IPdfReader pdfReader,
    IImageReader imageReader,
    IOfficeReader officeReader,
    IOfficeWriter officeWriter,
    IOptions<FileSystemOptions> opts,
    FileWriteLock writeLock,
    ScriptThrottle scriptThrottle,
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

    private string ScriptDisabledError() =>
        JsonSerializer.Serialize(new { error = "ScriptDisabled", message = "Script execution is disabled (AllowScript=false)." }, _json);

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
    [Description("Analyse an image and return metadata, quality metrics, and EXIF. Set includeBase64=true to get the full image as base64 (imageBase64 + imageMediaType) for passing to a vision LLM. Images larger than Base64MaxDimensionPx (default 1568) are automatically resized before encoding to save tokens.")]
    public string ReadImage(
        [Description("Absolute path to the image file")] string path,
        [Description("Return full image as base64 (imageBase64 + imageMediaType). Images wider/taller than Base64MaxDimensionPx are auto-resized first.")] bool includeBase64 = false,
        [Description("Override the max dimension for base64 resize (0 = use server default, typically 1568). Smaller values = smaller base64 = fewer tokens.")] int maxDimensionOverride = 0)
    {
        if (!filter.IsEnabled("read_image")) return DisabledError("read_image");
        if (!_opts.ImagesEnabled) return AccessError("Image reading is disabled (ImagesEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            var imageOpts = includeBase64
                ? new ImageOptions
                {
                    ExtractExif           = _opts.Image.ExtractExif,
                    ComputeQualityMetrics = _opts.Image.ComputeQualityMetrics,
                    ReturnBase64          = true,
                    Base64MaxDimensionPx  = maxDimensionOverride > 0 ? maxDimensionOverride : _opts.Image.Base64MaxDimensionPx,
                    ReturnThumbnail       = _opts.Image.ReturnThumbnail,
                    ThumbnailMaxDimension = _opts.Image.ThumbnailMaxDimension,
                    MaxImageFileSizeBytes = _opts.Image.MaxImageFileSizeBytes,
                    BlurThreshold         = _opts.Image.BlurThreshold,
                    ExposureUnderThreshold = _opts.Image.ExposureUnderThreshold,
                    ExposureOverThreshold  = _opts.Image.ExposureOverThreshold
                }
                : _opts.Image;
            var result = imageReader.Analyze(canonical, imageOpts);
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

            // Strip glob prefix (**/  or **\) — Directory.EnumerateFiles handles recursion itself
            // and throws on ** in the pattern. e.g. **/*.json → *.json
            var filePattern = pattern;
            if (filePattern.StartsWith("**/") || filePattern.StartsWith("**\\"))
                filePattern = filePattern[3..];
            else if (filePattern == "**" || filePattern == "**/*" || filePattern == "**\\*")
                filePattern = "*";

            var results = Directory.EnumerateFiles(canonical, filePattern,
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
            using (writeLock.Acquire(canonical))
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

    // ── Archive tools (zip, tar, tar.gz, 7z, rar, gz, bz2, xz …) ──────────

    [McpServerTool(Name = "list_zip")]
    [Description("List all entries inside an archive. Supports .zip, .tar, .tar.gz, .tgz, .tar.bz2, .tar.xz, .7z, .rar, .gz, .bz2. Returns nested paths, sizes, and compressed sizes.")]
    public string ListZip([Description("Absolute path to the archive file")] string path)
    {
        if (!filter.IsEnabled("list_zip")) return DisabledError("list_zip");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            if (!File.Exists(canonical)) return IoError($"File not found: {canonical}");

            using var archive = ArchiveFactory.OpenArchive(canonical);
            var entries = archive.Entries
                .OrderBy(e => e.Key)
                .Select(e => new
                {
                    fullName        = e.Key ?? string.Empty,
                    name            = Path.GetFileName(e.Key ?? string.Empty),
                    isDirectory     = e.IsDirectory,
                    sizeBytes       = e.Size,
                    compressedBytes = e.CompressedSize,
                    lastModified    = e.LastModifiedTime?.ToString("O")
                })
                .ToList();

            LogDebug("list_zip", ctx, t);
            return JsonSerializer.Serialize(
                new { archivePath = canonical, format = archive.Type.ToString(), entryCount = entries.Count, entries }, _json);
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("list_zip access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("list_zip bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (InvalidOperationException ex)   { logger.LogWarning("list_zip unsupported format: {Msg}", ex.Message); return IoError($"Unsupported or invalid archive: {ex.Message}"); }
        catch (IOException ex)                 { logger.LogWarning("list_zip io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "read_zip_entry")]
    [Description("Read the text content of a specific file entry inside an archive (.zip, .tar, .tar.gz, .7z, .rar, etc.). Use list_zip first to get entry paths.")]
    public string ReadZipEntry(
        [Description("Absolute path to the archive file")] string zipPath,
        [Description("Entry path inside the archive as returned by list_zip, e.g. folder/subfolder/file.txt")] string entryName)
    {
        if (!filter.IsEnabled("read_zip_entry")) return DisabledError("read_zip_entry");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(zipPath);
            if (!File.Exists(canonical)) return IoError($"File not found: {canonical}");

            using var archive = ArchiveFactory.OpenArchive(canonical);
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Key, entryName, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
                return IoError($"Entry '{entryName}' not found. Use list_zip to see available entries.");

            if (entry.IsDirectory)
                return IoError($"'{entryName}' is a directory entry, not a file.");

            if (entry.Size > _opts.MaxReadFileSizeBytes)
                return IoError($"Entry too large: {entry.Size} bytes (max {_opts.MaxReadFileSizeBytes}).");

            using var stream = entry.OpenEntryStream();
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = reader.ReadToEnd();

            LogDebug("read_zip_entry", ctx, t);
            return content;
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("read_zip_entry access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("read_zip_entry bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (InvalidOperationException ex)   { logger.LogWarning("read_zip_entry unsupported format: {Msg}", ex.Message); return IoError($"Unsupported or invalid archive: {ex.Message}"); }
        catch (IOException ex)                 { logger.LogWarning("read_zip_entry io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "copy_file")]
    [Description("Copy a file to a new location. Requires AllowWrites=true.")]
    public string CopyFile(
        [Description("Absolute source file path")] string sourcePath,
        [Description("Absolute destination file path")] string destinationPath,
        [Description("Overwrite destination if it exists (default false)")] bool overwrite = false)
    {
        if (!filter.IsEnabled("copy_file")) return DisabledError("copy_file");
        if (!_opts.AllowWrites) return WriteDisabledError();
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonicalSrc  = guard.Validate(sourcePath);
            var canonicalDest = guard.Validate(destinationPath);

            if (!File.Exists(canonicalSrc)) return IoError($"Source file not found: {canonicalSrc}");
            using (writeLock.Acquire(canonicalDest))
                File.Copy(canonicalSrc, canonicalDest, overwrite);
            LogDebug("copy_file", ctx, t);
            return "ok";
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("copy_file access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("copy_file bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("copy_file io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "append_file")]
    [Description("Append text content to an existing file (or create it if it does not exist). Requires AllowWrites=true.")]
    public string AppendFile(
        [Description("Absolute path to the file")] string path,
        [Description("Text content to append")] string content)
    {
        if (!filter.IsEnabled("append_file")) return DisabledError("append_file");
        if (!_opts.AllowWrites) return WriteDisabledError();
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            using (writeLock.Acquire(canonical))
                File.AppendAllText(canonical, content, System.Text.Encoding.UTF8);
            LogDebug("append_file", ctx, t);
            return "ok";
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("append_file access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("append_file bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("append_file io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "delete_directory")]
    [Description("Delete a directory. Set recursive=true to delete non-empty directories. Requires AllowWrites=true.")]
    public string DeleteDirectory(
        [Description("Absolute path to the directory")] string path,
        [Description("Delete all contents recursively (default false)")] bool recursive = false)
    {
        if (!filter.IsEnabled("delete_directory")) return DisabledError("delete_directory");
        if (!_opts.AllowWrites) return WriteDisabledError();
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            if (!Directory.Exists(canonical)) return IoError($"Directory not found: {canonical}");
            Directory.Delete(canonical, recursive);
            LogDebug("delete_directory", ctx, t);
            return "ok";
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("delete_directory access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("delete_directory bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("delete_directory io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "run_script")]
    [Description("Execute a bash script and return stdout, stderr, and exit code. Write scripts using bash syntax — works on Linux, macOS, and Windows (Git Bash / WSL). Requires AllowScript=true in config.")]
    public string RunScript([Description("Bash script to execute")] string script)
    {
        if (!filter.IsEnabled("run_script")) return DisabledError("run_script");
        if (!_opts.AllowScript) return ScriptDisabledError();
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var bash = ResolveBash();
            if (bash is null)
                return IoError("bash not found. Install Git Bash (Windows) or ensure bash is on PATH.");

            using var _ = scriptThrottle.Acquire(5000);

            var psi = new ProcessStartInfo
            {
                FileName = bash,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute  = false,
                CreateNoWindow   = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start bash at '{bash}'.");

            process.StandardInput.Write(script);
            process.StandardInput.Close();

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var timeoutMs = _opts.ScriptTimeoutSeconds * 1000;

            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill(entireProcessTree: true);
                return IoError($"Script timed out after {_opts.ScriptTimeoutSeconds}s.");
            }

            LogDebug("run_script", ctx, t);
            return JsonSerializer.Serialize(
                new { exitCode = process.ExitCode, stdout = stdout.Result, stderr = stderr.Result }, _json);
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("run_script access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (Exception ex)                   { logger.LogWarning("run_script error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    private static string? ResolveBash()
    {
        // Prefer bash already on PATH (covers Linux, macOS, WSL, Git Bash in PATH)
        if (IsOnPath("bash")) return "bash";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Common Git Bash install locations on Windows
            string[] candidates =
            [
                @"C:\Program Files\Git\bin\bash.exe",
                @"C:\Program Files (x86)\Git\bin\bash.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Git\bin\bash.exe")
            ];
            return candidates.FirstOrDefault(File.Exists);
        }

        return null;
    }

    private static bool IsOnPath(string executable)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute  = false,
                CreateNoWindow   = true
            });
            p?.WaitForExit(2000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    // ── Office read tools ──────────────────────────────────────────────────

    [McpServerTool(Name = "read_document")]
    [Description("Read a .docx file. Returns formatted text with # headings and | pipe tables |. " +
                 "Use summaryOnly=true for heading outline only (~10x fewer tokens). " +
                 "Use startParagraph/maxParagraphs to paginate large documents.")]
    public string ReadDocument(
        [Description("Absolute path to the .docx file")] string path,
        [Description("Return heading outline only (no body text). Much smaller response.")] bool summaryOnly = false,
        [Description("Zero-based paragraph index to start from (for pagination)")] int startParagraph = 0,
        [Description("Max paragraphs to return (0 = all)")] int maxParagraphs = 0)
    {
        if (!filter.IsEnabled("read_document")) return DisabledError("read_document");
        if (!_opts.OfficeEnabled) return AccessError("Office file reading is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            var result = officeReader.ReadDocument(canonical, _opts.Office, summaryOnly, startParagraph, maxParagraphs);
            LogDebug("read_document", ctx, t);
            return result;
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("read_document access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("read_document bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("read_document io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "read_spreadsheet")]
    [Description("Read a .xlsx file. Returns JSON with sheet names, column headers, and rows. " +
                 "Use summaryOnly=true for headers + row counts only (~50x fewer tokens). " +
                 "Use startRow/maxRows to paginate large sheets.")]
    public string ReadSpreadsheet(
        [Description("Absolute path to the .xlsx file")] string path,
        [Description("Return sheet names + column headers + row counts only (no data rows).")] bool summaryOnly = false,
        [Description("Zero-based row index to start from (for pagination)")] int startRow = 0,
        [Description("Max rows to return per sheet (0 = server default)")] int maxRows = 0)
    {
        if (!filter.IsEnabled("read_spreadsheet")) return DisabledError("read_spreadsheet");
        if (!_opts.OfficeEnabled) return AccessError("Office file reading is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            var result = officeReader.ReadSpreadsheet(canonical, _opts.Office, summaryOnly, startRow, maxRows);
            LogDebug("read_spreadsheet", ctx, t);
            return result;
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("read_spreadsheet access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("read_spreadsheet bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("read_spreadsheet io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "read_presentation")]
    [Description("Read a .pptx file. Returns slide text with '--- Slide N ---' markers. " +
                 "Use summaryOnly=true for slide titles only. Use startSlide/slideCount to paginate.")]
    public string ReadPresentation(
        [Description("Absolute path to the .pptx file")] string path,
        [Description("Return slide titles only (no bullet body or speaker notes).")] bool summaryOnly = false,
        [Description("Zero-based slide index to start from (for pagination)")] int startSlide = 0,
        [Description("Number of slides to return (0 = server default max)")] int slideCount = 0)
    {
        if (!filter.IsEnabled("read_presentation")) return DisabledError("read_presentation");
        if (!_opts.OfficeEnabled) return AccessError("Office file reading is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            var result = officeReader.ReadPresentation(canonical, _opts.Office, summaryOnly, startSlide, slideCount);
            LogDebug("read_presentation", ctx, t);
            return result;
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("read_presentation access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("read_presentation bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("read_presentation io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "search_in_document")]
    [Description("Search for text inside a .docx, .xlsx, or .pptx file. " +
                 "Returns a compact JSON list of matches with location info (paragraph index, sheet+cell, slide number). " +
                 "Use this before replace_in_* to find the exact text to target.")]
    public string SearchInDocument(
        [Description("Absolute path to the .docx, .xlsx, or .pptx file")] string path,
        [Description("Text to search for")] string searchText,
        [Description("Case-sensitive search (default false)")] bool caseSensitive = false)
    {
        if (!filter.IsEnabled("search_in_document")) return DisabledError("search_in_document");
        if (!_opts.OfficeEnabled) return AccessError("Office file reading is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            var result = officeReader.SearchInDocument(canonical, searchText, caseSensitive);
            LogDebug("search_in_document", ctx, t);
            return result;
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("search_in_document access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("search_in_document bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("search_in_document io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    // ── Office write tools ─────────────────────────────────────────────────

    [McpServerTool(Name = "write_document")]
    [Description("Create or overwrite a .docx from Markdown-like content. " +
                 "'# ' → Heading1, '## ' → Heading2, '### ' → Heading3. " +
                 "**bold**, *italic*, __underline__, ~~strikethrough~~, {color:RRGGBB}text{/color}, {size:N}text{/size}. " +
                 "Pipe-table blocks (lines starting with |) become Word tables. Requires AllowWrites=true.")]
    public string WriteDocument(
        [Description("Absolute path to the .docx file (created or overwritten)")] string path,
        [Description("Markdown-like content for the document")] string content)
    {
        if (!filter.IsEnabled("write_document")) return DisabledError("write_document");
        if (!_opts.AllowWrites) return WriteDisabledError();
        if (!_opts.OfficeEnabled) return AccessError("Office file writing is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            using (writeLock.Acquire(canonical))
            {
                var result = officeWriter.WriteDocument(canonical, content);
                LogDebug("write_document", ctx, t);
                return result;
            }
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("write_document access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("write_document bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("write_document io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "append_to_document")]
    [Description("Append formatted content to the end of an existing .docx. " +
                 "Supports same Markdown-like syntax as write_document. Requires AllowWrites=true.")]
    public string AppendToDocument(
        [Description("Absolute path to the existing .docx file")] string path,
        [Description("Content to append (same Markdown-like syntax as write_document)")] string content)
    {
        if (!filter.IsEnabled("append_to_document")) return DisabledError("append_to_document");
        if (!_opts.AllowWrites) return WriteDisabledError();
        if (!_opts.OfficeEnabled) return AccessError("Office file writing is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            using (writeLock.Acquire(canonical))
            {
                var result = officeWriter.AppendToDocument(canonical, content);
                LogDebug("append_to_document", ctx, t);
                return result;
            }
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("append_to_document access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("append_to_document bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("append_to_document io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "replace_in_document")]
    [Description("Find and replace text in an existing .docx. " +
                 "maxReplacements=0 replaces all; maxReplacements=1 replaces only the first occurrence. " +
                 "Call read_document first to identify the exact text to replace. Requires AllowWrites=true.")]
    public string ReplaceInDocument(
        [Description("Absolute path to the .docx file")] string path,
        [Description("Exact text to find")] string oldText,
        [Description("Replacement text")] string newText,
        [Description("Max replacements (0 = all, 1 = first only)")] int maxReplacements = 0)
    {
        if (!filter.IsEnabled("replace_in_document")) return DisabledError("replace_in_document");
        if (!_opts.AllowWrites) return WriteDisabledError();
        if (!_opts.OfficeEnabled) return AccessError("Office file writing is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            using (writeLock.Acquire(canonical))
            {
                var result = officeWriter.ReplaceInDocument(canonical, oldText, newText, maxReplacements);
                LogDebug("replace_in_document", ctx, t);
                return result;
            }
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("replace_in_document access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("replace_in_document bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("replace_in_document io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "write_spreadsheet")]
    [Description("Create or overwrite a .xlsx from a JSON array of arrays (rows × columns). " +
                 "First row is the header. Cell values starting with '=' are written as Excel formulas. " +
                 "Example: [[\"Name\",\"Score\"],[\"Alice\",\"=B2*1.1\"]] Requires AllowWrites=true.")]
    public string WriteSpreadsheet(
        [Description("Absolute path to the .xlsx file (created or overwritten)")] string path,
        [Description("JSON array of arrays: [[header1,header2],[val1,val2],...]")] string jsonRows)
    {
        if (!filter.IsEnabled("write_spreadsheet")) return DisabledError("write_spreadsheet");
        if (!_opts.AllowWrites) return WriteDisabledError();
        if (!_opts.OfficeEnabled) return AccessError("Office file writing is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            using (writeLock.Acquire(canonical))
            {
                var result = officeWriter.WriteSpreadsheet(canonical, jsonRows);
                LogDebug("write_spreadsheet", ctx, t);
                return result;
            }
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("write_spreadsheet access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("write_spreadsheet bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("write_spreadsheet io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "update_cells")]
    [Description("Update specific cells in an existing .xlsx. Values starting with '=' are written as formulas. " +
                 "Example: [{\"sheet\":\"Sheet1\",\"cell\":\"B11\",\"value\":\"=SUM(B2:B10)\"}] Requires AllowWrites=true.")]
    public string UpdateCells(
        [Description("Absolute path to the .xlsx file")] string path,
        [Description("JSON array: [{\"sheet\":\"Sheet1\",\"cell\":\"A1\",\"value\":\"new\"},...]")] string jsonCellUpdates)
    {
        if (!filter.IsEnabled("update_cells")) return DisabledError("update_cells");
        if (!_opts.AllowWrites) return WriteDisabledError();
        if (!_opts.OfficeEnabled) return AccessError("Office file writing is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            using (writeLock.Acquire(canonical))
            {
                var result = officeWriter.UpdateCells(canonical, jsonCellUpdates);
                LogDebug("update_cells", ctx, t);
                return result;
            }
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("update_cells access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("update_cells bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("update_cells io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "create_pivot_summary")]
    [Description("Compute a cross-tab pivot summary from a source sheet and write it to a new sheet. " +
                 "Spec: {sourceSheet, rowField, valueField, aggregation (sum/count/avg/min/max), " +
                 "columnField (optional), outputSheet}. Requires AllowWrites=true.")]
    public string CreatePivotSummary(
        [Description("Absolute path to the .xlsx file")] string path,
        [Description("Pivot spec JSON: {sourceSheet,rowField,valueField,aggregation,columnField?,outputSheet}")] string jsonPivotSpec)
    {
        if (!filter.IsEnabled("create_pivot_summary")) return DisabledError("create_pivot_summary");
        if (!_opts.AllowWrites) return WriteDisabledError();
        if (!_opts.OfficeEnabled) return AccessError("Office file writing is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            using (writeLock.Acquire(canonical))
            {
                var result = officeWriter.CreatePivotSummary(canonical, jsonPivotSpec);
                LogDebug("create_pivot_summary", ctx, t);
                return result;
            }
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("create_pivot_summary access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("create_pivot_summary bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("create_pivot_summary io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "write_presentation")]
    [Description("Create a new .pptx from a JSON slide array. Each slide: {title, bullets, notes}. " +
                 "Bullets starting with '  - ' become sub-bullets. Requires AllowWrites=true.")]
    public string WritePresentation(
        [Description("Absolute path to the .pptx file (created or overwritten)")] string path,
        [Description("JSON array: [{\"title\":\"Title\",\"bullets\":[\"Point\",\"  - Sub\"],\"notes\":\"...\"},...]")] string jsonSlides)
    {
        if (!filter.IsEnabled("write_presentation")) return DisabledError("write_presentation");
        if (!_opts.AllowWrites) return WriteDisabledError();
        if (!_opts.OfficeEnabled) return AccessError("Office file writing is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            using (writeLock.Acquire(canonical))
            {
                var result = officeWriter.WritePresentation(canonical, jsonSlides);
                LogDebug("write_presentation", ctx, t);
                return result;
            }
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("write_presentation access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("write_presentation bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("write_presentation io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "append_slides")]
    [Description("Append slides to an existing .pptx. Same JSON format as write_presentation. Requires AllowWrites=true.")]
    public string AppendSlides(
        [Description("Absolute path to the existing .pptx file")] string path,
        [Description("JSON array of slides to append: [{\"title\":\"Title\",\"bullets\":[...],\"notes\":\"...\"},...]")] string jsonSlides)
    {
        if (!filter.IsEnabled("append_slides")) return DisabledError("append_slides");
        if (!_opts.AllowWrites) return WriteDisabledError();
        if (!_opts.OfficeEnabled) return AccessError("Office file writing is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            using (writeLock.Acquire(canonical))
            {
                var result = officeWriter.AppendSlides(canonical, jsonSlides);
                LogDebug("append_slides", ctx, t);
                return result;
            }
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("append_slides access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("append_slides bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("append_slides io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "replace_in_presentation")]
    [Description("Find and replace text across all slides (titles, body, speaker notes) in an existing .pptx. " +
                 "maxReplacements=0 replaces all; 1 replaces only the first. Requires AllowWrites=true.")]
    public string ReplaceInPresentation(
        [Description("Absolute path to the .pptx file")] string path,
        [Description("Exact text to find")] string oldText,
        [Description("Replacement text")] string newText,
        [Description("Max replacements (0 = all, 1 = first only)")] int maxReplacements = 0)
    {
        if (!filter.IsEnabled("replace_in_presentation")) return DisabledError("replace_in_presentation");
        if (!_opts.AllowWrites) return WriteDisabledError();
        if (!_opts.OfficeEnabled) return AccessError("Office file writing is disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical = guard.Validate(path);
            using (writeLock.Acquire(canonical))
            {
                var result = officeWriter.ReplaceInPresentation(canonical, oldText, newText, maxReplacements);
                LogDebug("replace_in_presentation", ctx, t);
                return result;
            }
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("replace_in_presentation access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("replace_in_presentation bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("replace_in_presentation io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }

    [McpServerTool(Name = "convert_to_pdf")]
    [Description("Convert a .docx, .xlsx, or .pptx file to PDF using LibreOffice headless. " +
                 "LibreOffice must be installed on the server (free download: libreoffice.org). " +
                 "Returns {\"pdfPath\":\"...\"} with the path to the generated PDF. " +
                 "outputDirectory defaults to the same folder as the source file. Requires AllowWrites=true.")]
    public string ConvertToPdf(
        [Description("Absolute path to the .docx, .xlsx, or .pptx file")] string path,
        [Description("Output directory for the PDF (defaults to same folder as source)")] string? outputDirectory = null)
    {
        if (!filter.IsEnabled("convert_to_pdf")) return DisabledError("convert_to_pdf");
        if (!_opts.AllowWrites) return WriteDisabledError();
        if (!_opts.OfficeEnabled) return AccessError("Office tools are disabled (OfficeEnabled=false).");
        var ctx = McpServerContext.FromHttpContext(http);
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var canonical  = guard.Validate(path);
            var canonicalOutDir = outputDirectory is not null ? guard.Validate(outputDirectory) : null;
            var result = officeWriter.ConvertToPdf(canonical, canonicalOutDir);
            LogDebug("convert_to_pdf", ctx, t);
            return result;
        }
        catch (UnauthorizedAccessException ex) { logger.LogWarning("convert_to_pdf access denied: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (ArgumentException ex)           { logger.LogWarning("convert_to_pdf bad path: {Msg}", ex.Message); return AccessError(ex.Message); }
        catch (IOException ex)                 { logger.LogWarning("convert_to_pdf io error: {Msg}", ex.Message); return IoError(ex.Message); }
    }
}
