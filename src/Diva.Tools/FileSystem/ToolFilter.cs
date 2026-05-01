using Diva.Tools.FileSystem.Abstractions;
using Microsoft.Extensions.Options;

namespace Diva.Tools.FileSystem;

public sealed class ToolFilter(IOptions<FileSystemOptions> options) : IToolFilter
{
    private readonly FileSystemOptions _opts = options.Value;

    public bool IsEnabled(string toolName) =>
        _opts.EnabledTools.Count == 0 ||
        _opts.EnabledTools.Contains(toolName, StringComparer.OrdinalIgnoreCase);
}
