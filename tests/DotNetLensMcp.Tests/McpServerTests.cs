using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace DotNetLensMcp.Tests;

public class McpServerTests
{
    private readonly McpServer _server = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private JsonObject ParseResponse(object? response)
    {
        response.Should().NotBeNull();
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        return JsonSerializer.Deserialize<JsonObject>(json)!;
    }

    private JsonObject ParseError(string requestJson)
    {
        var response = ParseResponse(_server.HandleRequestAsync(requestJson).GetAwaiter().GetResult());
        return response["error"]!.AsObject();
    }

    [Fact]
    public async Task HandleRequest_WithIntegerId_EchoesIdBack()
    {
        var request = """{"jsonrpc":"2.0","id":42,"method":"initialize"}""";
        var response = ParseResponse(await _server.HandleRequestAsync(request));

        response["id"]!.GetValue<long>().Should().Be(42);
        response["jsonrpc"]!.GetValue<string>().Should().Be("2.0");
    }

    [Fact]
    public async Task HandleRequest_WithStringId_EchoesIdBack()
    {
        var request = """{"jsonrpc":"2.0","id":"abc-123","method":"initialize"}""";
        var response = ParseResponse(await _server.HandleRequestAsync(request));

        response["id"]!.GetValue<string>().Should().Be("abc-123");
    }

    [Fact]
    public async Task HandleRequest_WithGuidId_EchoesIdBack()
    {
        var request = """{"jsonrpc":"2.0","id":"550e8400-e29b-41d4-a716-446655440000","method":"initialize"}""";
        var response = ParseResponse(await _server.HandleRequestAsync(request));

        response["id"]!.GetValue<string>().Should().Be("550e8400-e29b-41d4-a716-446655440000");
    }

    [Fact]
    public async Task HandleRequest_Initialize_ReturnsProtocolVersion()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"initialize"}""";
        var response = ParseResponse(await _server.HandleRequestAsync(request));

        var result = response["result"]!.AsObject();
        result["protocolVersion"]!.GetValue<string>().Should().NotBeEmpty();
        result["serverInfo"]!.AsObject()["name"]!.GetValue<string>().Should().Be("DotNetLensMcp");
        result["serverInfo"]!.AsObject()["version"]!.GetValue<string>().Should().NotBe("1.0.0");
    }

    [Fact]
    public async Task HandleRequest_ToolsList_ReturnsTools()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";
        var response = ParseResponse(await _server.HandleRequestAsync(request));

        var result = response["result"]!.AsObject();
        var tools = result["tools"]!.AsArray();
        tools.Count.Should().BeGreaterThan(0);
        tools.Select(tool => tool!["name"]!.GetValue<string>()).Should().Contain([
            "roslyn_health_check",
            "roslyn_load_solution",
            "roslyn_get_symbol_info",
            "roslyn_rename_symbol",
            "roslyn_get_generated_code"]);
    }

    [Fact]
    public async Task HandleRequest_UnknownMethod_ReturnsMethodNotFound()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"unknown/method"}""";
        var response = ParseResponse(await _server.HandleRequestAsync(request));

        var error = response["error"]!.AsObject();
        error["code"]!.GetValue<int>().Should().Be(-32601);
        error["message"]!.GetValue<string>().Should().Contain("Method not found");
    }

    [Fact]
    public async Task HandleRequest_MissingMethod_ReturnsInvalidRequest()
    {
        var request = """{"jsonrpc":"2.0","id":1}""";
        var response = ParseResponse(await _server.HandleRequestAsync(request));

        var error = response["error"]!.AsObject();
        error["code"]!.GetValue<int>().Should().Be(-32600);
    }

    [Fact]
    public async Task HandleRequest_InvalidJson_ReturnsParseError()
    {
        var request = "not valid json";
        var response = ParseResponse(await _server.HandleRequestAsync(request));

        var error = response["error"]!.AsObject();
        error["code"]!.GetValue<int>().Should().Be(-32700);
    }

    [Fact]
    public async Task HandleRequest_MissingId_ResponseContainsIdField()
    {
        var request = """{"jsonrpc":"2.0","method":"initialize"}""";
        var json = JsonSerializer.Serialize(await _server.HandleRequestAsync(request), _jsonOptions);

        // Response should contain "id" field (JSON-RPC requires it in responses)
        json.Should().Contain("\"id\":");
    }

    [Fact]
    public async Task HandleRequest_ToolCallWithoutSolution_ReturnsError()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn:health_check","arguments":{}}}""";
        var response = ParseResponse(await _server.HandleRequestAsync(request));

        response["id"]!.GetValue<long>().Should().Be(1);
        response["result"].Should().NotBeNull();
    }

    [Fact]
    public async Task HandleRequest_ToolCallWithLegacyColonName_NormalizesToolName()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn:health_check","arguments":{}}}""";
        var response = ParseResponse(await _server.HandleRequestAsync(request));

        response["error"].Should().BeNull();
        response["result"].Should().NotBeNull();
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_load_solution","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_symbol_info","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_symbol_info","arguments":{"filePath":"C:\\Temp\\Test.cs","column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_search_symbols","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_go_to_definition","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_find_references","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_find_implementations","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_type_hierarchy","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_method_overloads","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_containing_member","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_find_callers","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_code_fixes","arguments":{"diagnosticId":"CS1002","line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_apply_code_fix","arguments":{"diagnosticId":"CS1002","line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_code_actions_at_position","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_apply_code_action_by_title","arguments":{"filePath":"C:\\Temp\\Test.cs","line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_rename_symbol","arguments":{"filePath":"C:\\Temp\\Test.cs","line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_organize_usings","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_extract_interface","arguments":{"filePath":"C:\\Temp\\Test.cs","line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_analyze_data_flow","arguments":{"filePath":"C:\\Temp\\Test.cs","endLine":1}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_analyze_control_flow","arguments":{"filePath":"C:\\Temp\\Test.cs","startLine":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_missing_members","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_outgoing_calls","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_validate_code","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_check_type_compatibility","arguments":{"sourceType":"Derived"}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_instantiation_options","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_analyze_change_impact","arguments":{"filePath":"C:\\Temp\\Test.cs","line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_method_source","arguments":{"typeName":"MyType"}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_method_source_batch","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_type_members","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_method_signature","arguments":{"typeName":"MyType"}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_attributes","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_derived_types","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_base_types","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_type_overview","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_analyze_method","arguments":{"typeName":"MyType"}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_generate_constructor","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_change_signature","arguments":{"filePath":"C:\\Temp\\Test.cs","line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_extract_method","arguments":{"filePath":"C:\\Temp\\Test.cs","startLine":0,"endLine":1}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_implement_missing_members","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_encapsulate_field","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_inline_variable","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_extract_variable","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_complexity_metrics","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_add_null_checks","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_generate_equality_members","arguments":{"line":0,"column":0}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_type_members_batch","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_find_attribute_usages","arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_generated_code","arguments":{"projectName":"TestProject"}}}""")]
    public void HandleRequest_ToolCallWithMissingRequiredParameter_ReturnsInvalidParams(string request)
    {
        var error = ParseError(request);

        error["code"]!.GetValue<int>().Should().Be(-32602);
        error["message"]!.GetValue<string>().Should().Contain("Invalid params");
    }

    [Fact]
    public void HandleRequest_ToolCallWithInvalidStringArrayParameterType_ReturnsInvalidParams()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_semantic_query","arguments":{"kinds":"Method"}}}""";

        var error = ParseError(request);

        error["code"]!.GetValue<int>().Should().Be(-32602);
        error["message"]!.GetValue<string>().Should().Contain("Invalid params");
    }

    [Fact]
    public void HandleRequest_ToolCallWithInvalidRequiredParameterType_ReturnsInvalidParams()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_symbol_info","arguments":{"filePath":"C:\\Temp\\Test.cs","line":"0","column":0}}}""";

        var error = ParseError(request);

        error["code"]!.GetValue<int>().Should().Be(-32602);
        error["message"]!.GetValue<string>().Should().Contain("Invalid params");
    }

    [Fact]
    public void HandleRequest_ToolCallWithInvalidOptionalParameterType_ReturnsInvalidParams()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_find_references","arguments":{"filePath":"C:\\Temp\\Test.cs","line":0,"column":0,"maxResults":"many"}}}""";

        var error = ParseError(request);

        error["code"]!.GetValue<int>().Should().Be(-32602);
        error["message"]!.GetValue<string>().Should().Contain("Invalid params");
    }

    [Fact]
    public void HandleRequest_ToolCallWithInvalidOptionalBooleanParameterType_ReturnsInvalidParams()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_get_code_actions_at_position","arguments":{"filePath":"C:\\Temp\\Test.cs","line":0,"column":0,"includeCodeFixes":"yes"}}}""";

        var error = ParseError(request);

        error["code"]!.GetValue<int>().Should().Be(-32602);
        error["message"]!.GetValue<string>().Should().Contain("Invalid params");
    }

    [Fact]
    public void HandleRequest_ToolCallWithInvalidRefactoringOptionalParameterType_ReturnsInvalidParams()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_generate_constructor","arguments":{"filePath":"C:\\Temp\\Test.cs","line":0,"column":0,"includeProperties":"yes"}}}""";

        var error = ParseError(request);

        error["code"]!.GetValue<int>().Should().Be(-32602);
        error["message"]!.GetValue<string>().Should().Contain("Invalid params");
    }

    [Fact]
    public void HandleRequest_ToolCallWithUnknownTool_ReturnsInvalidParams()
    {
        var request = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_unknown_tool","arguments":{}}}""";

        var error = ParseError(request);

        error["code"]!.GetValue<int>().Should().Be(-32602);
        error["message"]!.GetValue<string>().Should().Contain("unknown tool");
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":42,"arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"roslyn_health_check","arguments":"invalid"}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":"invalid"}""")]
    public void HandleRequest_ToolCallWithInvalidEnvelopeParameterType_ReturnsInvalidParams(string request)
    {
        var error = ParseError(request);

        error["code"]!.GetValue<int>().Should().Be(-32602);
        error["message"]!.GetValue<string>().Should().Contain("Invalid params");
    }

    [Fact]
    public async Task HandleRequest_NotificationInitialized_ReturnsNull()
    {
        var request = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
        var response = await _server.HandleRequestAsync(request);

        response.Should().BeNull();
    }

    [Fact]
    public async Task HandleRequest_NotificationWithNoId_ReturnsNull()
    {
        var request = """{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":"abc"}}""";
        var response = await _server.HandleRequestAsync(request);

        response.Should().BeNull();
    }
}
