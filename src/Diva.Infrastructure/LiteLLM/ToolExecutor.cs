using System.Text.Json;
using Diva.Core.Configuration;
using Diva.Infrastructure.LiteLLM;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Diva.Infrastructure.LiteLLM;

/// <summary>
/// Encapsulates the low-level MCP tool invocation: timeout, error classification, and result
/// truncation. Extracted from <see cref="AnthropicAgentRunner"/> for independent testability.
/// Registered as Singleton (ILogger + IOptions&lt;AgentOptions&gt; dependencies — stateless).
/// </summary>
public sealed class ToolExecutor(ILogger<ToolExecutor> logger, IOptions<AgentOptions> agentOptions)
{
    /// <summary>
    /// Calls one MCP tool and returns the (possibly truncated) text output.
    /// </summary>
    /// <returns>
    /// <c>Output</c> — text content (never null/empty; contains an error message on failure).
    /// <c>Failed</c> — true when the result was an MCP error, looked like an error string, or timed out.
    /// <c>Error</c> — the underlying exception; null for MCP-level errors reported in <c>Output</c>.
    /// </returns>
    public async Task<(string Output, bool Failed, Exception? Error)> ExecuteAsync(
        string toolName,
        string inputJson,
        Dictionary<string, McpClient> toolClientMap,
        Dictionary<string, McpClient> mcpClients,
        int maxToolResultChars,
        CancellationToken ct)
    {
        var startTime         = DateTime.UtcNow;
        var toolTimeoutSeconds = agentOptions.Value.ToolTimeoutSeconds;
        logger.LogInformation("Starting tool execution: {ToolName} at {Time}",
            toolName, startTime.ToString("HH:mm:ss.fff"));

        try
        {
            var owningClient = toolClientMap.GetValueOrDefault(toolName)
                ?? mcpClients.Values.First();
            using var toolCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            toolCts.CancelAfter(TimeSpan.FromSeconds(toolTimeoutSeconds));

            Dictionary<string, object?> toolArgs;
            try
            {
                toolArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(inputJson) ?? [];
            }
            catch (JsonException argEx)
            {
                logger.LogWarning(argEx,
                    "Tool {ToolName}: input JSON is malformed — calling with empty arguments. Input: {Input}",
                    toolName, inputJson.Length > 200 ? inputJson[..200] + "…" : inputJson);
                toolArgs = [];
            }

            var callResult = await owningClient.CallToolAsync(
                toolName,
                toolArgs,
                cancellationToken: toolCts.Token);

            var output = ReActToolHelper.TruncateResult(
                string.Join("\n", callResult.Content.OfType<TextContentBlock>().Select(c => c.Text)),
                maxToolResultChars);

            var failed = (callResult.IsError == true) || ReActToolHelper.IsToolOutputError(output);

            var endTime = DateTime.UtcNow;
            logger.LogInformation(
                "Completed tool execution: {ToolName} at {Time} (duration: {Duration}ms)",
                toolName, endTime.ToString("HH:mm:ss.fff"),
                (endTime - startTime).TotalMilliseconds.ToString("F0"));

            return (output, failed, null);
        }
        catch (OperationCanceledException)
        {
            var endTime = DateTime.UtcNow;
            logger.LogWarning(
                "Tool execution timed out: {ToolName} at {Time} (duration: {Duration}ms)",
                toolName, endTime.ToString("HH:mm:ss.fff"),
                (endTime - startTime).TotalMilliseconds.ToString("F0"));

            return (
                $"Tool '{toolName}' timed out after {toolTimeoutSeconds}s. Try a narrower query (e.g. shorter date range).",
                true,
                new TimeoutException($"Tool '{toolName}' timed out after {toolTimeoutSeconds}s."));
        }
        catch (Exception ex)
        {
            var endTime = DateTime.UtcNow;
            logger.LogWarning(ex,
                "Tool execution failed: {ToolName} at {Time} (duration: {Duration}ms)",
                toolName, endTime.ToString("HH:mm:ss.fff"),
                (endTime - startTime).TotalMilliseconds.ToString("F0"));

            return ($"Error: {ex.Message}", true, ex);
        }
    }
}
