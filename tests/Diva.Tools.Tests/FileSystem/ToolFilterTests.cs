using Diva.Tools.FileSystem;
using Diva.Tools.Tests.Helpers;

namespace Diva.Tools.Tests.FileSystem;

public sealed class ToolFilterTests
{
    [Fact]
    public void EmptyEnabledTools_AllToolsEnabled()
    {
        var filter = new ToolFilter(McpToolsTestFixtures.AsOptions(new FileSystemOptions()));

        Assert.True(filter.IsEnabled("read_file"));
        Assert.True(filter.IsEnabled("write_file"));
        Assert.True(filter.IsEnabled("delete_file"));
        Assert.True(filter.IsEnabled("any_tool"));
    }

    [Fact]
    public void ConfiguredEnabledTools_OnlyListedPass()
    {
        var opts = new FileSystemOptions { EnabledTools = ["read_file", "list_directory"] };
        var filter = new ToolFilter(McpToolsTestFixtures.AsOptions(opts));

        Assert.True(filter.IsEnabled("read_file"));
        Assert.True(filter.IsEnabled("list_directory"));
        Assert.False(filter.IsEnabled("write_file"));
        Assert.False(filter.IsEnabled("delete_file"));
    }

    [Fact]
    public void ToolName_CaseInsensitive()
    {
        var opts = new FileSystemOptions { EnabledTools = ["read_file"] };
        var filter = new ToolFilter(McpToolsTestFixtures.AsOptions(opts));

        Assert.True(filter.IsEnabled("READ_FILE"));
        Assert.True(filter.IsEnabled("Read_File"));
        Assert.True(filter.IsEnabled("read_file"));
    }

    [Fact]
    public void DisabledTool_ReturnsFalse()
    {
        var opts = new FileSystemOptions { EnabledTools = ["read_file"] };
        var filter = new ToolFilter(McpToolsTestFixtures.AsOptions(opts));

        Assert.False(filter.IsEnabled("get_image_info"));
    }

    [Fact]
    public void SingleTool_AllOthersDisabled()
    {
        var opts = new FileSystemOptions { EnabledTools = ["get_allowed_roots"] };
        var filter = new ToolFilter(McpToolsTestFixtures.AsOptions(opts));

        Assert.True(filter.IsEnabled("get_allowed_roots"));
        Assert.False(filter.IsEnabled("read_file"));
        Assert.False(filter.IsEnabled("search_files"));
    }

    [Fact]
    public void AllTools_Enabled_WhenListIsEmpty()
    {
        var opts = new FileSystemOptions { EnabledTools = [] };
        var filter = new ToolFilter(McpToolsTestFixtures.AsOptions(opts));

        foreach (var tool in new[]
        {
            "read_file","read_pdf","get_image_info","read_image","list_directory",
            "get_file_info","search_files","get_allowed_roots",
            "write_file","create_directory","delete_file","move_item"
        })
        {
            Assert.True(filter.IsEnabled(tool), $"Expected '{tool}' to be enabled");
        }
    }
}
