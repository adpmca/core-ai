using Diva.Tools.FileSystem;
using Diva.Tools.FileSystem.Abstractions;
using Diva.Tools.FileSystem.Readers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Diva.Tools.Tests.Helpers;

public static class McpToolsTestFixtures
{
    /// <summary>Wraps an <see cref="FileSystemOptions"/> value in IOptions.</summary>
    public static IOptions<FileSystemOptions> AsOptions(FileSystemOptions opts) =>
        Options.Create(opts);

    /// <summary>Creates a non-production IHostEnvironment substitute.</summary>
    public static IHostEnvironment DevEnvironment()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        return env;
    }

    /// <summary>Creates a production IHostEnvironment substitute.</summary>
    public static IHostEnvironment ProdEnvironment()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Production");
        return env;
    }

    /// <summary>
    /// Builds a real <see cref="FileSystemMcpTools"/> over a temp directory.
    /// The caller is responsible for cleaning up the temp dir.
    /// </summary>
    public static (FileSystemMcpTools tools, string tempDir) BuildOverTempDir(
        FileSystemOptions? opts = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"diva-fs-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        opts ??= new FileSystemOptions
        {
            AllowedBasePaths = [tempDir],
            AllowWrites = true
        };

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        var tools = new FileSystemMcpTools(
            accessor,
            new FileSystemPathGuard(AsOptions(opts), DevEnvironment(),
                NullLogger<FileSystemPathGuard>.Instance),
            new ToolFilter(AsOptions(opts)),
            new PdfReader(NullLogger<PdfReader>.Instance),
            new ImageReader(NullLogger<ImageReader>.Instance),
            AsOptions(opts),
            NullLogger<FileSystemMcpTools>.Instance);

        return (tools, tempDir);
    }

    /// <summary>Reads an embedded test resource as a byte array.</summary>
    public static byte[] GetEmbeddedResource(string fileName)
    {
        var asm = typeof(McpToolsTestFixtures).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource '{fileName}' not found.");

        using var stream = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
