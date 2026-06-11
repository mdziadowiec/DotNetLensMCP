using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetLensMcp;

public class McpServer : IDisposable
{
    private const string ProtocolVersion = "2024-11-05";
    private static readonly string ServerVersion = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static readonly string[] LogLevels = { "Debug", "Information", "Warning", "Error" };

    private readonly RoslynService _roslynService;
    private readonly McpToolCallHandler _toolCallHandler;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _logLevel;

    public McpServer()
    {
        _roslynService = new RoslynService();
        _toolCallHandler = new McpToolCallHandler(_roslynService);
        _logLevel = Environment.GetEnvironmentVariable("ROSLYN_LOG_LEVEL") ?? "Information";
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Converters = { new RequestId.Converter() }
        };
    }

    public async Task RunAsync()
    {
        await LogAsync("Information", "Roslyn MCP Server starting...");

        // Auto-load solution from environment variable
        var solutionPath = Environment.GetEnvironmentVariable("DOTNET_SOLUTION_PATH");
        if (!string.IsNullOrEmpty(solutionPath))
        {
            try
            {
                // If it's a directory, try to find a .sln or .slnx file
                if (Directory.Exists(solutionPath))
                {
                    var slnFiles = Directory.GetFiles(solutionPath, "*.sln")
                        .Concat(Directory.GetFiles(solutionPath, "*.slnx"))
                        .ToArray();
                    if (slnFiles.Length > 0)
                    {
                        solutionPath = slnFiles[0];
                    }
                }

                if (File.Exists(solutionPath))
                {
                    await LogAsync("Information", $"Auto-loading solution: {solutionPath}");
                    await _roslynService.LoadSolutionAsync(solutionPath);
                }
            }
            catch (Exception ex)
            {
                await LogAsync("Warning", $"Failed to auto-load solution: {ex.Message}");
            }
        }

        // Main event loop - read from stdin, write to stdout
        using var reader = Console.In;
        using var writer = Console.Out;

        while (true)
        {
            try
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    await LogAsync("Information", "Received EOF on stdin, shutting down");
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                await LogAsync("Debug", $"Received request: {line}");

                var response = await HandleRequestAsync(line);

                if (response != null)
                {
                    var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                    await writer.WriteLineAsync(responseJson);
                    await writer.FlushAsync();

                    await LogAsync("Debug", $"Sent response: {responseJson}");
                }
            }
            catch (Exception ex)
            {
                await LogAsync("Error", $"Error in main loop: {ex}");
            }
        }
    }

    internal async Task<object?> HandleRequestAsync(string requestJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(requestJson, _jsonOptions);
            if (request == null)
            {
                return JsonRpc.CreateErrorResponse(null, JsonRpc.ErrorCodes.ParseError, "Parse error");
            }

            var id = request.Id;
            var method = request.Method;
            var paramsNode = request.Params;

            if (string.IsNullOrEmpty(method))
            {
                return JsonRpc.CreateErrorResponse(id, JsonRpc.ErrorCodes.InvalidRequest, "Invalid Request: missing method");
            }

            // Notifications have no id and require no response
            if (id == null && method.StartsWith("notifications/"))
            {
                await LogAsync("Debug", $"Received notification: {method}");
                return null;
            }

            return method switch
            {
                "initialize" => await HandleInitializeAsync(id),
                "tools/list" => await HandleListToolsAsync(id),
                "tools/call" => await HandleToolCallAsync(id, paramsNode),
                _ => JsonRpc.CreateErrorResponse(id, JsonRpc.ErrorCodes.MethodNotFound, $"Method not found: {method}")
            };
        }
        catch (JsonException ex)
        {
            await LogAsync("Error", $"Error parsing request: {ex}");
            return JsonRpc.CreateErrorResponse(null, JsonRpc.ErrorCodes.ParseError, "Parse error");
        }
        catch (Exception ex)
        {
            await LogAsync("Error", $"Error handling request: {ex}");
            return JsonRpc.CreateErrorResponse(null, JsonRpc.ErrorCodes.InternalError, $"Internal error: {ex.Message}");
        }
    }

    private Task<object> HandleInitializeAsync(RequestId? id)
    {
        var response = JsonRpc.CreateSuccessResponse(id, new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "DotNetLensMcp",
                version = ServerVersion
            }
        });
        return Task.FromResult((object)response);
    }

    private Task<object> HandleListToolsAsync(RequestId? id)
    {
        var tools = ToolCatalog.GetTools();
        return Task.FromResult((object)JsonRpc.CreateSuccessResponse(id, new { tools }));
    }

    private async Task<object> HandleToolCallAsync(RequestId? id, JsonNode? paramsNode)
    {
        try
        {
            var paramsObject = GetToolCallParamsObject(paramsNode);
            var toolCallParams = new ToolArguments(paramsObject);
            var name = McpToolNames.Normalize(toolCallParams.OptionalString("name"));
            var arguments = toolCallParams.OptionalObject("arguments");

            if (string.IsNullOrEmpty(name))
            {
                return JsonRpc.CreateErrorResponse(id, JsonRpc.ErrorCodes.InvalidParams, "Invalid params: missing tool name");
            }

            var toolArguments = new ToolArguments(arguments);

            var result = await _toolCallHandler.HandleAsync(name, toolArguments);

            // Wrap result in MCP content format
            var mpcResult = new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(result, _jsonOptions)
                    }
                }
            };

            return JsonRpc.CreateSuccessResponse(id, mpcResult);
        }
        catch (FileNotFoundException ex)
        {
            await LogAsync("Error", $"File not found: {ex.Message}");
            return JsonRpc.CreateErrorResponse(id, JsonRpc.ErrorCodes.InvalidParams, $"File not found: {ex.Message}");
        }
        catch (ToolArgumentException ex)
        {
            await LogAsync("Warning", $"Invalid tool arguments: {ex.Message}");
            return JsonRpc.CreateErrorResponse(id, JsonRpc.ErrorCodes.InvalidParams, ex.Message);
        }
        catch (OperationCanceledException)
        {
            var timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("ROSLYN_TIMEOUT_SECONDS"), out var t) ? t : 30;
            await LogAsync("Warning", $"Tool call timed out after {timeoutSeconds}s");
            var timeoutResult = new
            {
                success = false,
                error = new
                {
                    Code = ErrorCodes.Timeout,
                    Message = $"Operation timed out after {timeoutSeconds}s (ROSLYN_TIMEOUT_SECONDS)",
                    Hint = "Increase ROSLYN_TIMEOUT_SECONDS or narrow the scope of the operation"
                }
            };
            var mpcResult = new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = System.Text.Json.JsonSerializer.Serialize(timeoutResult, _jsonOptions)
                    }
                }
            };
            return JsonRpc.CreateSuccessResponse(id, mpcResult);
        }
        catch (Exception ex)
        {
            await LogAsync("Error", $"Error executing tool: {ex}");
            return JsonRpc.CreateErrorResponse(id, JsonRpc.ErrorCodes.InternalError, $"Internal error: {ex.Message}");
        }
    }

    private static JsonObject? GetToolCallParamsObject(JsonNode? paramsNode)
    {
        try
        {
            return paramsNode?.AsObject();
        }
        catch (InvalidOperationException ex)
        {
            throw ToolArgumentException.Invalid("params", typeof(JsonObject), ex);
        }
    }

    private async Task LogAsync(string level, string message)
    {
        if (ShouldLog(level))
        {
            await Console.Error.WriteLineAsync($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
        }
    }

    private bool ShouldLog(string messageLevel)
    {
        var messageIndex = Array.IndexOf(LogLevels, messageLevel);
        var configuredIndex = Array.IndexOf(LogLevels, _logLevel);
        return messageIndex >= configuredIndex;
    }

    /// <inheritdoc/>
    public void Dispose() => _roslynService.Dispose();
}
