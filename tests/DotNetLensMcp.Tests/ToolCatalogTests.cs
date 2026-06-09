using FluentAssertions;
using System.Text.Json.Nodes;
using Xunit;

namespace DotNetLensMcp.Tests;

public class ToolCatalogTests
{
    [Fact]
    public async Task ToolCatalog_AllToolsHaveHandlerRoutes()
    {
        var handler = new McpToolCallHandler(new RoslynService());
        var catalogToolNames = ToolCatalog.GetTools().Select(tool => tool.Name).ToList();

        catalogToolNames.Should().OnlyHaveUniqueItems();

        foreach (var toolName in catalogToolNames)
        {
            try
            {
                await handler.HandleAsync(toolName, new ToolArguments(new JsonObject()));
            }
            catch (ToolArgumentException ex)
            {
                ex.Message.Should().NotContain("unknown tool", because: $"catalog tool '{toolName}' should have a handler route");
            }
            catch (InvalidOperationException)
            {
                // Expected for tools that require a loaded solution before validating detailed arguments.
            }
        }
    }
}
