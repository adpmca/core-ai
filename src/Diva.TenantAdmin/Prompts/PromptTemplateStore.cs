using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Diva.TenantAdmin.Prompts;

/// <summary>
/// Loads versioned prompt template files from the prompts/ directory.
/// Files are resolved relative to the solution root using the host's ContentRootPath.
/// File naming convention: prompts/{category}/{name}.txt
/// </summary>
public sealed class PromptTemplateStore
{
    private readonly string _promptsRoot;
    private readonly ILogger<PromptTemplateStore> _logger;

    public PromptTemplateStore(IHostEnvironment env, ILogger<PromptTemplateStore> logger)
    {
        _logger = logger;
        // ContentRootPath = .../src/Diva.Host/ → go up twice to reach solution root
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, "prompts"),
            Path.Combine(env.ContentRootPath, "..", "..", "prompts"),
            Path.Combine(env.ContentRootPath, "..", "..", "..", "prompts"),
            Path.Combine(AppContext.BaseDirectory, "prompts"),
        };

        _promptsRoot = Array.Find(candidates, Directory.Exists)
            ?? Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "prompts"));

        _logger.LogDebug("PromptTemplateStore root resolved to: {Root}", _promptsRoot);
    }

    /// <summary>Test/override constructor that accepts an explicit root path.</summary>
    public PromptTemplateStore(string promptsRoot, ILogger<PromptTemplateStore> logger)
    {
        _promptsRoot = promptsRoot;
        _logger = logger;
    }

    public async Task<string> GetAsync(string category, string name, CancellationToken ct)
    {
        // Security: strip path navigation characters to prevent traversal
        var safeCategory = Path.GetFileName(category);
        var safeName = Path.GetFileName(name);
        var path = Path.GetFullPath(Path.Combine(_promptsRoot, safeCategory, $"{safeName}.txt"));
        var root = Path.GetFullPath(_promptsRoot);

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Path traversal attempt blocked: {Category}/{Name}", category, name);
            return string.Empty;
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning("Prompt template not found: {Path}", path);
            return string.Empty;
        }

        return await File.ReadAllTextAsync(path, ct);
    }
}
