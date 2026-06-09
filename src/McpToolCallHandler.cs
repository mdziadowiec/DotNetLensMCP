using System.Text.Json.Nodes;

namespace DotNetLensMcp;

internal sealed class McpToolCallHandler
{
    private readonly record struct SourcePositionArguments(string FilePath, int Line, int Column);

    private readonly record struct SourceRangeArguments(string FilePath, int StartLine, int EndLine);

    private readonly record struct SelectionRangeArguments(int? EndLine, int? EndColumn);

    private readonly record struct MethodLookupArguments(string TypeName, string MethodName, int? OverloadIndex);

    private readonly RoslynService _roslynService;

    internal McpToolCallHandler(RoslynService roslynService)
    {
        _roslynService = roslynService;
    }

    internal async Task<object> HandleAsync(string name, ToolArguments arguments)
    {
        return name switch
        {
            "roslyn_health_check" => await _roslynService.GetHealthCheckAsync(),

            "roslyn_load_solution" => await _roslynService.LoadSolutionAsync(
                arguments.RequiredString("solutionPath")),

            "roslyn_sync_documents" => await _roslynService.SyncDocumentsAsync(
                arguments.OptionalStringList("filePaths")),

            "roslyn_get_symbol_info" => await GetSymbolInfoAsync(arguments),

            "roslyn_go_to_definition" => await GoToDefinitionAsync(arguments),

            "roslyn_find_references" => await FindReferencesAsync(arguments),

            "roslyn_find_implementations" => await FindImplementationsAsync(arguments),

            "roslyn_get_type_hierarchy" => await GetTypeHierarchyAsync(arguments),

            "roslyn_search_symbols" => await _roslynService.SearchSymbolsAsync(
                arguments.RequiredString("query"),
                arguments.OptionalString("kind"),
                arguments.OptionalInt32("maxResults") ?? 50,
                arguments.OptionalString("namespaceFilter"),
                arguments.OptionalInt32("offset") ?? 0),

            "roslyn_semantic_query" => await _roslynService.SemanticQueryAsync(
                arguments.OptionalStringList("kinds"),
                arguments.OptionalBoolean("isAsync"),
                arguments.OptionalString("namespaceFilter"),
                arguments.OptionalString("accessibility"),
                arguments.OptionalBoolean("isStatic"),
                arguments.OptionalString("type"),
                arguments.OptionalString("returnType"),
                arguments.OptionalStringList("attributes"),
                arguments.OptionalStringList("parameterIncludes"),
                arguments.OptionalStringList("parameterExcludes"),
                arguments.OptionalInt32("maxResults")),

            "roslyn_get_diagnostics" => await _roslynService.GetDiagnosticsAsync(
                arguments.OptionalString("filePath"),
                arguments.OptionalString("projectPath"),
                arguments.OptionalString("severity"),
                arguments.OptionalBoolean("includeHidden", false)),

            "roslyn_get_code_fixes" => await GetCodeFixesAsync(arguments),

            "roslyn_apply_code_fix" => await ApplyCodeFixAsync(arguments),

            "roslyn_get_project_structure" => await _roslynService.GetProjectStructureAsync(
                arguments.OptionalBoolean("includeReferences", true),
                arguments.OptionalBoolean("includeDocuments", false),
                arguments.OptionalString("projectNamePattern"),
                arguments.OptionalInt32("maxProjects"),
                arguments.OptionalBoolean("summaryOnly", false)),

            "roslyn_organize_usings" => await _roslynService.OrganizeUsingsAsync(
                arguments.RequiredString("filePath")),

            "roslyn_organize_usings_batch" => await _roslynService.OrganizeUsingsBatchAsync(
                arguments.OptionalString("projectName"),
                arguments.OptionalString("filePattern"),
                arguments.OptionalBoolean("preview", true)),

            "roslyn_format_document_batch" => await _roslynService.FormatDocumentBatchAsync(
                arguments.OptionalString("projectName"),
                arguments.OptionalBoolean("includeTests", true),
                arguments.OptionalBoolean("preview", true)),

            "roslyn_get_method_overloads" => await GetMethodOverloadsAsync(arguments),

            "roslyn_get_containing_member" => await GetContainingMemberAsync(arguments),

            "roslyn_find_callers" => await FindCallersAsync(arguments),

            "roslyn_find_unused_code" => await _roslynService.FindUnusedCodeAsync(
                arguments.OptionalString("projectName"),
                arguments.OptionalBoolean("includePrivate", true),
                arguments.OptionalBoolean("includeInternal", false),
                arguments.OptionalString("symbolKindFilter"),
                arguments.OptionalInt32("maxResults")),

            "roslyn_rename_symbol" => await RenameSymbolAsync(arguments),

            "roslyn_extract_interface" => await ExtractInterfaceAsync(arguments),

            "roslyn_dependency_graph" => await _roslynService.GetDependencyGraphAsync(
                arguments.OptionalString("format")),

            // ============ NEW TOOLS: Name-Based Type Discovery ============

            "roslyn_get_type_members" => await _roslynService.GetTypeMembersAsync(
                arguments.RequiredString("typeName"),
                arguments.OptionalBoolean("includeInherited", false),
                arguments.OptionalString("memberKind"),
                arguments.OptionalString("verbosity") ?? "compact",
                arguments.OptionalInt32("maxResults") ?? 100),

            "roslyn_get_method_signature" => await GetMethodSignatureAsync(arguments),

            "roslyn_get_attributes" => await _roslynService.GetAttributesAsync(
                arguments.RequiredString("attributeName"),
                arguments.OptionalString("scope"),
                arguments.OptionalBoolean("parseGodotHints", true),
                arguments.OptionalInt32("maxResults") ?? 100),

            "roslyn_get_derived_types" => await _roslynService.GetDerivedTypesAsync(
                arguments.RequiredString("baseTypeName"),
                arguments.OptionalBoolean("includeTransitive", true),
                arguments.OptionalInt32("maxResults") ?? 100),

            "roslyn_get_base_types" => await _roslynService.GetBaseTypesAsync(
                arguments.RequiredString("typeName")),

            "roslyn_analyze_data_flow" => await AnalyzeDataFlowAsync(arguments),

            "roslyn_analyze_control_flow" => await AnalyzeControlFlowAsync(arguments),

            // ============ COMPOUND TOOLS ============

            "roslyn_get_type_overview" => await _roslynService.GetTypeOverviewAsync(
                arguments.RequiredString("typeName")),

            "roslyn_analyze_method" => await _roslynService.AnalyzeMethodAsync(
                arguments.RequiredString("typeName"),
                arguments.RequiredString("methodName"),
                arguments.OptionalBoolean("includeCallers", true),
                arguments.OptionalBoolean("includeOutgoingCalls", false),
                arguments.OptionalInt32("maxCallers") ?? 20,
                arguments.OptionalInt32("maxOutgoingCalls") ?? 50),

            "roslyn_get_file_overview" => await _roslynService.GetFileOverviewAsync(
                arguments.RequiredString("filePath")),

            // Phase 2: AI-Focused Tools
            "roslyn_get_missing_members" => await GetMissingMembersAsync(arguments),

            "roslyn_get_outgoing_calls" => await GetOutgoingCallsAsync(arguments),

            "roslyn_validate_code" => await _roslynService.ValidateCodeAsync(
                arguments.RequiredString("code"),
                arguments.OptionalString("contextFilePath"),
                arguments.OptionalBoolean("standalone", false),
                arguments.OptionalString("language")),

            "roslyn_check_type_compatibility" => await _roslynService.CheckTypeCompatibilityAsync(
                arguments.RequiredString("sourceType"),
                arguments.RequiredString("targetType")),

            "roslyn_get_instantiation_options" => await _roslynService.GetInstantiationOptionsAsync(
                arguments.RequiredString("typeName")),

            "roslyn_analyze_change_impact" => await AnalyzeChangeImpactAsync(arguments),

            "roslyn_get_method_source" => await GetMethodSourceAsync(arguments),

            "roslyn_get_method_source_batch" => await _roslynService.GetMethodSourceBatchAsync(
                ParseMethodBatchRequests(arguments.RequiredNode("methods")),
                arguments.OptionalInt32("maxMethods") ?? 20),

            "roslyn_generate_constructor" => await GenerateConstructorAsync(arguments),

            "roslyn_change_signature" => await ChangeSignatureAsync(arguments),

            "roslyn_extract_method" => await ExtractMethodAsync(arguments),

            "roslyn_get_code_actions_at_position" => await GetCodeActionsAtPositionAsync(arguments),

            "roslyn_apply_code_action_by_title" => await ApplyCodeActionByTitleAsync(arguments),

            "roslyn_implement_missing_members" => await ImplementMissingMembersAsync(arguments),

            "roslyn_encapsulate_field" => await EncapsulateFieldAsync(arguments),

            "roslyn_inline_variable" => await InlineVariableAsync(arguments),

            "roslyn_extract_variable" => await ExtractVariableAsync(arguments),

            "roslyn_get_complexity_metrics" => await _roslynService.GetComplexityMetricsAsync(
                arguments.RequiredString("filePath"),
                arguments.OptionalInt32("line"),
                arguments.OptionalInt32("column"),
                arguments.OptionalStringList("metrics")),

            "roslyn_add_null_checks" => await AddNullChecksAsync(arguments),

            "roslyn_generate_equality_members" => await GenerateEqualityMembersAsync(arguments),

            "roslyn_get_type_members_batch" => await _roslynService.GetTypeMembersBatchAsync(
                arguments.RequiredStringList("typeNames"),
                arguments.OptionalBoolean("includeInherited", false),
                arguments.OptionalString("memberKind"),
                arguments.OptionalString("verbosity") ?? "compact",
                arguments.OptionalInt32("maxResultsPerType") ?? 50),

            "roslyn_find_attribute_usages" => await _roslynService.FindAttributeUsagesAsync(
                arguments.RequiredString("attributeName"),
                arguments.OptionalString("projectName"),
                arguments.OptionalInt32("maxResults") ?? 100),

            "roslyn_get_di_registrations" => await _roslynService.GetDiRegistrationsAsync(
                arguments.OptionalString("projectName")),

            "roslyn_find_reflection_usage" => await _roslynService.FindReflectionUsageAsync(
                arguments.OptionalString("projectName"),
                arguments.OptionalInt32("maxResults") ?? 100),

            "roslyn_find_circular_dependencies" => await _roslynService.FindCircularDependenciesAsync(
                arguments.OptionalString("level")),

            "roslyn_get_nuget_dependencies" => await _roslynService.GetNuGetDependenciesAsync(
                arguments.OptionalString("projectName")),

            "roslyn_get_source_generators" => await _roslynService.GetSourceGeneratorsAsync(
                arguments.OptionalString("projectName")),

            "roslyn_get_generated_code" => await _roslynService.GetGeneratedCodeAsync(
                arguments.RequiredString("projectName"),
                arguments.RequiredString("generatedFileName")),

            // ============ QUALITY ANALYSIS TOOLS ============

            "roslyn_find_large_classes" => await _roslynService.FindLargeClassesAsync(
                arguments.OptionalInt32("memberCountThreshold") ?? 20,
                arguments.OptionalInt32("lineCountThreshold") ?? 500,
                arguments.OptionalString("projectFilter")),

            "roslyn_get_public_api_surface" => await _roslynService.GetPublicApiSurfaceAsync(
                arguments.OptionalString("projectFilter"),
                arguments.OptionalBoolean("includeInternal", false)),

            "roslyn_get_operators" => await _roslynService.GetOperatorsAsync(
                arguments.RequiredString("typeName")),

            "roslyn_find_obsolete_usage" => await _roslynService.FindObsoleteUsageAsync(
                arguments.OptionalString("projectFilter"),
                arguments.OptionalInt32("maxResults") ?? 100),

            "roslyn_get_call_graph" => await _roslynService.GetCallGraphAsync(
                arguments.RequiredString("typeName"),
                arguments.RequiredString("methodName"),
                arguments.OptionalInt32("depth") ?? 3,
                arguments.OptionalString("direction") ?? "callees"),

            "roslyn_find_async_violations" => await _roslynService.FindAsyncViolationsAsync(
                arguments.OptionalString("projectFilter"),
                arguments.OptionalInt32("maxResults") ?? 100),

            "roslyn_find_disposable_misuse" => await _roslynService.FindDisposableMisuseAsync(
                arguments.OptionalString("projectFilter"),
                arguments.OptionalInt32("maxResults") ?? 100),

            "roslyn_find_event_subscribers" => await _roslynService.FindEventSubscribersAsync(
                arguments.OptionalString("typeName"),
                arguments.OptionalString("eventName"),
                arguments.OptionalString("projectFilter"),
                arguments.OptionalInt32("maxResults") ?? 100),

            "roslyn_find_naming_violations" => await _roslynService.FindNamingViolationsAsync(
                arguments.OptionalString("projectFilter"),
                arguments.OptionalInt32("maxResults") ?? 100),

            "roslyn_find_god_objects" => await _roslynService.FindGodObjectsAsync(
                arguments.OptionalInt32("memberThreshold") ?? 20,
                arguments.OptionalInt32("lineThreshold") ?? 500,
                arguments.OptionalInt32("outgoingNamespaceThreshold") ?? 5,
                arguments.OptionalString("projectFilter")),

            "roslyn_peek_il" => await _roslynService.PeekIlAsync(
                arguments.RequiredString("methodSymbol")),

            "roslyn_inspect_external_assembly" => await _roslynService.InspectExternalAssemblyAsync(
                arguments.RequiredString("assemblyName"),
                arguments.OptionalString("mode") ?? "summary",
                arguments.OptionalString("namespaceFilter")),

            _ => throw ToolArgumentException.UnknownTool(name)
            };
    }

    private static SourcePositionArguments GetSourcePosition(ToolArguments arguments) =>
        new(
            arguments.RequiredString("filePath"),
            arguments.RequiredInt32("line"),
            arguments.RequiredInt32("column"));

    private static SourceRangeArguments GetSourceRange(ToolArguments arguments) =>
        new(
            arguments.RequiredString("filePath"),
            arguments.RequiredInt32("startLine"),
            arguments.RequiredInt32("endLine"));

    private static SelectionRangeArguments GetSelectionRange(ToolArguments arguments) =>
        new(
            arguments.OptionalInt32("endLine"),
            arguments.OptionalInt32("endColumn"));

    private static MethodLookupArguments GetMethodLookup(ToolArguments arguments) =>
        new(
            arguments.RequiredString("typeName"),
            arguments.RequiredString("methodName"),
            arguments.OptionalInt32("overloadIndex"));

    private async Task<object> GetSymbolInfoAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.GetSymbolInfoAsync(position.FilePath, position.Line, position.Column);
    }

    private async Task<object> GoToDefinitionAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.GoToDefinitionAsync(position.FilePath, position.Line, position.Column);
    }

    private async Task<object> FindReferencesAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.FindReferencesAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalInt32("maxResults"));
    }

    private async Task<object> FindImplementationsAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.FindImplementationsAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalInt32("maxResults"));
    }

    private async Task<object> GetTypeHierarchyAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.GetTypeHierarchyAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalInt32("maxDerivedTypes"));
    }

    private async Task<object> GetMethodOverloadsAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.GetMethodOverloadsAsync(position.FilePath, position.Line, position.Column);
    }

    private async Task<object> GetContainingMemberAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.GetContainingMemberAsync(position.FilePath, position.Line, position.Column);
    }

    private async Task<object> FindCallersAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.FindCallersAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalInt32("maxResults"));
    }

    private async Task<object> GetCodeFixesAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.GetCodeFixesAsync(
            position.FilePath,
            arguments.RequiredString("diagnosticId"),
            position.Line,
            position.Column);
    }

    private async Task<object> ApplyCodeFixAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.ApplyCodeFixAsync(
            position.FilePath,
            arguments.RequiredString("diagnosticId"),
            position.Line,
            position.Column,
            arguments.OptionalInt32("fixIndex"),
            arguments.OptionalBoolean("preview", true));
    }

    private async Task<object> RenameSymbolAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.RenameSymbolAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.RequiredString("newName"),
            arguments.OptionalBoolean("preview", true),
            arguments.OptionalInt32("maxFiles"),
            arguments.OptionalString("verbosity"));
    }

    private async Task<object> ExtractInterfaceAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.ExtractInterfaceAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.RequiredString("interfaceName"),
            arguments.OptionalStringList("includeMemberNames"));
    }

    private async Task<object> GetMethodSignatureAsync(ToolArguments arguments)
    {
        var method = GetMethodLookup(arguments);
        return await _roslynService.GetMethodSignatureAsync(method.TypeName, method.MethodName, method.OverloadIndex);
    }

    private async Task<object> GetMethodSourceAsync(ToolArguments arguments)
    {
        var method = GetMethodLookup(arguments);
        return await _roslynService.GetMethodSourceAsync(method.TypeName, method.MethodName, method.OverloadIndex);
    }

    private async Task<object> AnalyzeDataFlowAsync(ToolArguments arguments)
    {
        var range = GetSourceRange(arguments);
        return await _roslynService.AnalyzeDataFlowAsync(range.FilePath, range.StartLine, range.EndLine);
    }

    private async Task<object> AnalyzeControlFlowAsync(ToolArguments arguments)
    {
        var range = GetSourceRange(arguments);
        return await _roslynService.AnalyzeControlFlowAsync(range.FilePath, range.StartLine, range.EndLine);
    }

    private async Task<object> ExtractMethodAsync(ToolArguments arguments)
    {
        var range = GetSourceRange(arguments);
        return await _roslynService.ExtractMethodAsync(
            range.FilePath,
            range.StartLine,
            range.EndLine,
            arguments.RequiredString("methodName"),
            arguments.OptionalString("accessibility") ?? "private",
            arguments.OptionalBoolean("preview", true));
    }

    private async Task<object> GetMissingMembersAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.GetMissingMembersAsync(position.FilePath, position.Line, position.Column);
    }

    private async Task<object> GetOutgoingCallsAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.GetOutgoingCallsAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalInt32("maxDepth"));
    }

    private async Task<object> GenerateConstructorAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.GenerateConstructorAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalBoolean("includeProperties", false),
            arguments.OptionalBoolean("initializeToDefault", false));
    }

    private async Task<object> GetCodeActionsAtPositionAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        var selection = GetSelectionRange(arguments);
        return await _roslynService.GetCodeActionsAtPositionAsync(
            position.FilePath,
            position.Line,
            position.Column,
            selection.EndLine,
            selection.EndColumn,
            arguments.OptionalBoolean("includeCodeFixes", true),
            arguments.OptionalBoolean("includeRefactorings", true));
    }

    private async Task<object> ApplyCodeActionByTitleAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        var selection = GetSelectionRange(arguments);
        return await _roslynService.ApplyCodeActionByTitleAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.RequiredString("title"),
            selection.EndLine,
            selection.EndColumn,
            arguments.OptionalBoolean("preview", true));
    }

    private async Task<object> ImplementMissingMembersAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.ImplementMissingMembersAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalBoolean("preview", true));
    }

    private async Task<object> EncapsulateFieldAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.EncapsulateFieldAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalBoolean("preview", true));
    }

    private async Task<object> InlineVariableAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.InlineVariableAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalBoolean("preview", true));
    }

    private async Task<object> AddNullChecksAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.AddNullChecksAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalBoolean("preview", true));
    }

    private async Task<object> GenerateEqualityMembersAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.GenerateEqualityMembersAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.OptionalBoolean("includeOperators", true),
            arguments.OptionalBoolean("preview", true));
    }

    private async Task<object> AnalyzeChangeImpactAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.AnalyzeChangeImpactAsync(
            position.FilePath,
            position.Line,
            position.Column,
            arguments.RequiredString("changeType"),
            arguments.OptionalString("newValue"));
    }

    private async Task<object> ChangeSignatureAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        return await _roslynService.ChangeSignatureAsync(
            position.FilePath,
            position.Line,
            position.Column,
            ParseSignatureChanges(arguments.RequiredNode("changes")),
            arguments.OptionalBoolean("preview", true));
    }

    private async Task<object> ExtractVariableAsync(ToolArguments arguments)
    {
        var position = GetSourcePosition(arguments);
        var selection = GetSelectionRange(arguments);
        return await _roslynService.ExtractVariableAsync(
            position.FilePath,
            position.Line,
            position.Column,
            selection.EndLine,
            selection.EndColumn,
            arguments.OptionalBoolean("preview", true));
    }

    private static List<SignatureChange> ParseSignatureChanges(JsonNode? changesNode)
    {
        var changes = new List<SignatureChange>();
        if (changesNode is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonObject obj)
                {
                    var change = new SignatureChange
                    {
                        Action = obj["action"]?.GetValue<string>() ?? "",
                        Name = obj["name"]?.GetValue<string>(),
                        Type = obj["type"]?.GetValue<string>(),
                        NewName = obj["newName"]?.GetValue<string>(),
                        DefaultValue = obj["defaultValue"]?.GetValue<string>(),
                        Position = obj["position"]?.GetValue<int>()
                    };

                    if (obj["order"] is JsonArray orderArray)
                    {
                        change.Order = orderArray.Select(o => o?.GetValue<string>() ?? "").ToList();
                    }

                    changes.Add(change);
                }
            }
        }
        return changes;
    }

    private static List<Dictionary<string, object>> ParseMethodBatchRequests(JsonNode? methodsNode)
    {
        var methods = new List<Dictionary<string, object>>();
        if (methodsNode is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonObject obj)
                {
                    var method = new Dictionary<string, object>();
                    if (obj["typeName"] != null)
                        method["typeName"] = obj["typeName"]!.GetValue<string>();
                    if (obj["methodName"] != null)
                        method["methodName"] = obj["methodName"]!.GetValue<string>();
                    if (obj["overloadIndex"] != null)
                        method["overloadIndex"] = obj["overloadIndex"]!.GetValue<int>();
                    methods.Add(method);
                }
            }
        }
        return methods;
    }

}
