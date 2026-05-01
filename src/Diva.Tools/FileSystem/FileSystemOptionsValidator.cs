using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Diva.Tools.FileSystem;

public sealed class FileSystemOptionsValidator(IHostEnvironment env)
    : IValidateOptions<FileSystemOptions>
{
    public ValidateOptionsResult Validate(string? name, FileSystemOptions options)
    {
        if (env.IsProduction() && options.AllowedBasePaths.Count == 0)
            return ValidateOptionsResult.Fail(
                "FileSystem:AllowedBasePaths must be set in production. " +
                "Leaving it empty allows access to the entire filesystem.");

        if (options.Image.BlurThreshold < 0)
            return ValidateOptionsResult.Fail(
                "FileSystem:Image:BlurThreshold must be >= 0.");

        if (options.MaxReadFileSizeBytes < 1)
            return ValidateOptionsResult.Fail(
                "FileSystem:MaxReadFileSizeBytes must be >= 1.");

        return ValidateOptionsResult.Success;
    }
}
