using System.IO.Enumeration;
using System.Runtime.InteropServices;
using Diva.Tools.FileSystem.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Diva.Tools.FileSystem;

public sealed class FileSystemPathGuard(
    IOptions<FileSystemOptions> options,
    IHostEnvironment env,
    ILogger<FileSystemPathGuard> logger) : IFileSystemPathGuard
{
    private readonly FileSystemOptions _opts = options.Value;
    private static readonly StringComparison _pathComparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public string Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.");

        // LLMs frequently generate incorrect paths. Normalize common cases against AllowedBasePaths:
        //   1. Unix-style absolute:   /Desktop/file.txt  → C:\Users\Admin\Desktop\file.txt
        //   2. Relative:              Desktop/file.txt   → C:\Users\Admin\Desktop\file.txt
        //   3. Wrong user/prefix:     C:\Users\ajay\Desktop\file.txt → C:\Users\Admin\Desktop\file.txt
        //      (any absolute Windows path whose dir structure contains the AllowedBasePath dir name)
        if (_opts.AllowedBasePaths.Count > 0)
            path = NormalizeLlmPath(path);


        if (!Path.IsPathRooted(path))
            throw new ArgumentException("Absolute path required. Relative paths are not allowed.");

        var resolved = Path.GetFullPath(path);

        var fileName = Path.GetFileName(resolved);
        foreach (var pattern in _opts.DenyFilePatterns)
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
                throw new UnauthorizedAccessException(
                    $"Access denied: '{fileName}' matches a restricted file pattern.");
        }

        if (_opts.AllowedBasePaths.Count == 0)
        {
            if (env.IsProduction())
                throw new InvalidOperationException(
                    "FileSystem:AllowedBasePaths must be configured in production.");

            logger.LogWarning("FileSystemPathGuard: AllowedBasePaths is empty in non-production — all paths accessible");
        }
        else
        {
            var allowed = false;
            foreach (var basePath in _opts.AllowedBasePaths)
            {
                var normalised = Path.GetFullPath(basePath);
                // Append separator to prevent C:\Users matching C:\UsersDanger
                if (!normalised.EndsWith(Path.DirectorySeparatorChar))
                    normalised += Path.DirectorySeparatorChar;

                if (resolved.StartsWith(normalised, _pathComparison) ||
                    string.Equals(resolved, normalised.TrimEnd(Path.DirectorySeparatorChar), _pathComparison))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
                throw new UnauthorizedAccessException(
                    $"Access denied: path is outside all configured AllowedBasePaths.");
        }

        if (!_opts.FollowSymlinks)
        {
            var info = new FileInfo(resolved);
            if (info.Exists && info.LinkTarget is not null)
                throw new UnauthorizedAccessException(
                    "Access denied: symbolic links are not permitted (FollowSymlinks=false).");

            var dirInfo = new DirectoryInfo(resolved);
            if (dirInfo.Exists && dirInfo.LinkTarget is not null)
                throw new UnauthorizedAccessException(
                    "Access denied: symbolic links are not permitted (FollowSymlinks=false).");
        }

        return resolved;
    }

    private string NormalizeLlmPath(string path)
    {
        foreach (var basePath in _opts.AllowedBasePaths)
        {
            var baseNorm = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar);
            var baseName = Path.GetFileName(baseNorm); // e.g. "Desktop"

            // Case 1 & 2: Unix-style (/Desktop/file) or relative (Desktop/file)
            // First path component equals the AllowedBasePath directory name.
            if (!Path.IsPathRooted(path) || path.StartsWith('/'))
            {
                var stripped = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var sep = stripped.IndexOf(Path.DirectorySeparatorChar);
                var first = sep >= 0 ? stripped[..sep] : stripped;
                var rest  = sep >= 0 ? stripped[(sep + 1)..] : string.Empty;

                if (string.Equals(first, baseName, _pathComparison))
                    return string.IsNullOrEmpty(rest) ? baseNorm : Path.Combine(baseNorm, rest);
            }
            // Case 3: Absolute Windows path with wrong prefix (e.g. wrong username).
            // Find the AllowedBasePath dir name inside the path and remap everything after it.
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var needle = Path.DirectorySeparatorChar + baseName + Path.DirectorySeparatorChar;
                var idx = path.IndexOf(needle, _pathComparison);
                if (idx >= 0)
                {
                    var rest = path[(idx + needle.Length)..];
                    return string.IsNullOrEmpty(rest) ? baseNorm : Path.Combine(baseNorm, rest);
                }
                // Path ends with \Desktop (no trailing slash)
                if (path.EndsWith(Path.DirectorySeparatorChar + baseName, _pathComparison))
                    return baseNorm;
            }
        }
        return path;
    }

    public IReadOnlyList<string> GetAllowedRoots()
    {
        List<string> platformRoots;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            platformRoots = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        else
        {
            platformRoots = ["/"];
        }

        if (_opts.AllowedBasePaths.Count == 0)
            return platformRoots;

        // Intersect: return allowed base paths that exist on this system
        return _opts.AllowedBasePaths
            .Select(p => Path.GetFullPath(p))
            .Where(p => platformRoots.Any(r => p.StartsWith(r, _pathComparison)))
            .ToList();
    }
}
