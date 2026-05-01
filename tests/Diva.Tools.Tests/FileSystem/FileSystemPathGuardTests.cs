using Diva.Tools.FileSystem;
using Diva.Tools.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Diva.Tools.Tests.FileSystem;

public sealed class FileSystemPathGuardTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemPathGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"diva-guard-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FileSystemPathGuard BuildGuard(FileSystemOptions opts, bool production = false) =>
        new(McpToolsTestFixtures.AsOptions(opts),
            production ? McpToolsTestFixtures.ProdEnvironment() : McpToolsTestFixtures.DevEnvironment(),
            NullLogger<FileSystemPathGuard>.Instance);

    [Fact]
    public void ValidPath_ReturnsCanonical()
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [_tempDir] };
        var guard = BuildGuard(opts);
        var file = Path.Combine(_tempDir, "hello.txt");
        File.WriteAllText(file, "x");

        var result = guard.Validate(file);

        Assert.Equal(Path.GetFullPath(file), result);
    }

    [Fact]
    public void RelativePath_Throws()
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [_tempDir] };
        var guard = BuildGuard(opts);

        Assert.Throws<ArgumentException>(() => guard.Validate("relative/path.txt"));
    }

    [Fact]
    public void PathTraversal_IsBlocked()
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [_tempDir] };
        var guard = BuildGuard(opts);

        var traversal = Path.Combine(_tempDir, "..", "outside.txt");
        Assert.Throws<UnauthorizedAccessException>(() => guard.Validate(traversal));
    }

    [Fact]
    public void PathOutsideAllowedBase_IsBlocked()
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [_tempDir] };
        var guard = BuildGuard(opts);

        var outside = Path.GetTempPath();
        Assert.Throws<UnauthorizedAccessException>(() => guard.Validate(outside));
    }

    [Theory]
    [InlineData("secret.key")]
    [InlineData("cert.pfx")]
    [InlineData("cert.p12")]
    [InlineData("server.pem")]
    [InlineData("ca.cer")]
    [InlineData(".env")]
    [InlineData("id_rsa")]
    [InlineData("id_ed25519")]
    [InlineData("cluster.kubeconfig")]
    [InlineData("app.secret")]
    public void DeniedFilePattern_Throws(string fileName)
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [_tempDir] };
        var guard = BuildGuard(opts);
        var path = Path.Combine(_tempDir, fileName);

        Assert.Throws<UnauthorizedAccessException>(() => guard.Validate(path));
    }

    [Fact]
    public void AppsettingsJson_IsBlocked()
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [_tempDir] };
        var guard = BuildGuard(opts);

        Assert.Throws<UnauthorizedAccessException>(() =>
            guard.Validate(Path.Combine(_tempDir, "appsettings.json")));
        Assert.Throws<UnauthorizedAccessException>(() =>
            guard.Validate(Path.Combine(_tempDir, "appsettings.Development.json")));
    }

    [Fact]
    public void EmptyAllowedPaths_Production_Throws()
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [] };
        var guard = BuildGuard(opts, production: true);

        Assert.Throws<InvalidOperationException>(() =>
            guard.Validate(Path.Combine(_tempDir, "file.txt")));
    }

    [Fact]
    public void EmptyAllowedPaths_Dev_AllowsAllPaths()
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [] };
        var guard = BuildGuard(opts, production: false);
        var file = Path.Combine(_tempDir, "file.txt");

        var result = guard.Validate(file);

        Assert.Equal(Path.GetFullPath(file), result);
    }

    [Fact]
    public void AllowedBase_DoesNotMatchLongerPrefix()
    {
        // C:\allowed must not match C:\allowedextra
        var baseDir = Path.Combine(Path.GetTempPath(), $"base-{Guid.NewGuid():N}");
        var extDir  = Path.Combine(Path.GetTempPath(), $"base-{Guid.NewGuid():N}-extra");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(extDir);

        try
        {
            var opts = new FileSystemOptions { AllowedBasePaths = [baseDir] };
            var guard = BuildGuard(opts);

            Assert.Throws<UnauthorizedAccessException>(() =>
                guard.Validate(Path.Combine(extDir, "file.txt")));
        }
        finally
        {
            Directory.Delete(baseDir);
            Directory.Delete(extDir);
        }
    }

    [Fact]
    public void GetAllowedRoots_WithConfiguredPaths_ReturnsThem()
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [_tempDir] };
        var guard = BuildGuard(opts);

        var roots = guard.GetAllowedRoots();

        Assert.Contains(roots, r => r.StartsWith(Path.GetPathRoot(_tempDir)!,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetAllowedRoots_EmptyConfig_ReturnsPlatformRoots()
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [] };
        var guard = BuildGuard(opts);

        var roots = guard.GetAllowedRoots();

        Assert.NotEmpty(roots);
    }

    [Fact]
    public void Symlink_File_BlockedWhenFollowSymlinksFalse()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return; // skip on unsupported platforms

        var realFile = Path.Combine(_tempDir, "real.txt");
        var linkFile = Path.Combine(_tempDir, "link.txt");
        File.WriteAllText(realFile, "content");

        try
        {
            File.CreateSymbolicLink(linkFile, realFile);
        }
        catch (UnauthorizedAccessException)
        {
            return; // skip if no privilege to create symlinks
        }
        catch (IOException)
        {
            return;
        }

        var opts = new FileSystemOptions { AllowedBasePaths = [_tempDir], FollowSymlinks = false };
        var guard = BuildGuard(opts);

        Assert.Throws<UnauthorizedAccessException>(() => guard.Validate(linkFile));
    }

    [Fact]
    public void Validate_AllowedRootItself_ReturnsCanonical()
    {
        var opts = new FileSystemOptions { AllowedBasePaths = [_tempDir] };
        var guard = BuildGuard(opts);

        var result = guard.Validate(_tempDir);

        Assert.Equal(Path.GetFullPath(_tempDir), result);
    }
}
