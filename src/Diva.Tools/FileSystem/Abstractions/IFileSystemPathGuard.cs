namespace Diva.Tools.FileSystem.Abstractions;

public interface IFileSystemPathGuard
{
    /// <summary>
    /// Validates and resolves <paramref name="path"/> to a canonical absolute path.
    /// Throws <see cref="UnauthorizedAccessException"/> or <see cref="ArgumentException"/> on violation.
    /// </summary>
    string Validate(string path);

    /// <summary>Returns platform-appropriate root paths, intersected with AllowedBasePaths when configured.</summary>
    IReadOnlyList<string> GetAllowedRoots();
}
