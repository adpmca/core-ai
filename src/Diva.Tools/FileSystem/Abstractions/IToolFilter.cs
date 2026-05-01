namespace Diva.Tools.FileSystem.Abstractions;

public interface IToolFilter
{
    /// <summary>Returns true when the named tool is permitted by current configuration.</summary>
    bool IsEnabled(string toolName);
}
