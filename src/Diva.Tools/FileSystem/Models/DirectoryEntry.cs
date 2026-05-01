namespace Diva.Tools.FileSystem.Models;

public sealed record DirectoryEntry(
    string Name,
    string FullPath,
    string Type,        // "file" | "directory" | "symlink"
    long? SizeBytes,
    string Modified,
    bool IsReadOnly);
