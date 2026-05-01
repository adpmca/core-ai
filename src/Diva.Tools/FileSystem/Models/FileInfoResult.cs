namespace Diva.Tools.FileSystem.Models;

public sealed record FileInfoResult(
    string Name,
    string FullPath,
    long SizeBytes,
    string Created,
    string Modified,
    bool IsDirectory,
    bool IsReadOnly,
    bool IsSymlink,
    string? Extension);
