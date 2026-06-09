namespace DotNetLensMcp;

internal static class ToolCatalog
{
    private static readonly IReadOnlyList<ToolDescriptor> Tools =
        [
            new() {
                Name = "roslyn_health_check",
                Description = "Check the health and status of the Roslyn MCP server and workspace. Returns: server status, solution loaded state, project count, and memory usage. Call this first to verify the server is ready.",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new() {
                Name = "roslyn_load_solution",
                Description = "Load a .NET solution for analysis. MUST be called before using any other analysis tools. Returns: projectCount, documentCount, and load time. Use health_check to verify current state.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        solutionPath = new { type = "string", description = "Absolute path to .sln or .slnx file" }
                    },
                    required = new[] { "solutionPath" }
                }
            },
            new() {
                Name = "roslyn_sync_documents",
                Description = @"Synchronize document changes from disk into the loaded solution. Call this after using Edit/Write tools to ensure Roslyn has fresh content.

USAGE:
- sync_documents(filePaths: [""src/Foo.cs"", ""src/Bar.cs""]) - sync specific files
- sync_documents() - sync ALL documents (refresh entire solution)

WHEN TO CALL:
- After using Edit tool to modify .cs files
- After using Write tool to create new .cs files
- After deleting .cs files
- NOT needed after using DotNetLensMcp refactoring tools (they auto-update)

HANDLES: Modified files (updates content), new files (adds to solution), deleted files (removes from solution).
Much faster than load_solution - only updates documents, doesn't re-parse projects.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePaths = new {
                            type = "array",
                            items = new { type = "string" },
                            description = "Optional: specific file paths to sync. If omitted, syncs ALL documents from disk."
                        }
                    }
                }
            },
            new() {
                Name = "roslyn_get_symbol_info",
                Description = "Get detailed semantic information about a symbol at a specific position. IMPORTANT: Uses ZERO-BASED coordinates. If your editor shows 'Line 14, Column 5', pass line=13, column=4. Returns symbol kind, type, namespace, documentation, and location.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number (Visual Studio line 14 = line 13 here)" },
                        column = new { type = "integer", description = "Zero-based column number (Visual Studio col 5 = col 4 here)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_go_to_definition",
                Description = "Fast navigation to symbol definition. Returns the definition location without finding all references. IMPORTANT: Uses ZERO-BASED coordinates (editor line 10 = pass line 9).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file containing the symbol" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_find_references",
                Description = "Find all references to a symbol across the entire solution. Returns file paths, line numbers, and code context for each reference. IMPORTANT: Uses ZERO-BASED coordinates (editor line 10 = pass line 9).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file containing the symbol" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" },
                        maxResults = new { type = "integer", description = "Maximum number of references to return (default: 100). Results are truncated with a hint if limit is exceeded." }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_find_implementations",
                Description = "Find all implementations of an interface or abstract class. Returns implementing types with their locations. IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        maxResults = new { type = "integer", description = "Maximum number of implementations to return (default: 50). Results are truncated with a hint if limit is exceeded." }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_get_type_hierarchy",
                Description = "Get the inheritance hierarchy (base types and derived types) for a type. Returns baseTypes chain and derivedTypes list. IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        maxDerivedTypes = new { type = "integer", description = "Maximum number of derived types to return (default: 50). Results are truncated with a hint if limit is exceeded." }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_search_symbols",
                Description = "Search for types, methods, properties, etc. by name across the solution. Supports glob patterns (e.g., '*Handler' finds classes ending with 'Handler', 'Get*' finds symbols starting with 'Get'). Use ? for single character wildcard. PAGINATION: Returns totalCount and hasMore. Use offset to paginate through results.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Search query - supports wildcards: * (any characters), ? (single character). Examples: 'Handler', '*Handler', 'Get*', 'I?Service'. Case-insensitive." },
                        kind = new { type = "string", description = "Optional: filter by symbol kind. For types use: Class, Interface, Struct, Enum, Delegate. For members use: Method, Property, Field, Event. Other: Namespace. Case-insensitive." },
                        maxResults = new { type = "integer", description = "Maximum number of results per page (default: 50)" },
                        namespaceFilter = new { type = "string", description = "Optional: filter by namespace (supports wildcards). Examples: 'MyApp.Core.*', '*.Services', 'MyApp.*.Handlers'. Case-insensitive." },
                        offset = new { type = "integer", description = "Offset for pagination (default: 0). Use pagination.nextOffset from previous response to get next page." }
                    },
                    required = new[] { "query" }
                }
            },
            new() {
                Name = "roslyn_semantic_query",
                Description = @"Advanced semantic code query with multiple filters. Find symbols based on their semantic properties.

EXAMPLES:
- Async methods without CancellationToken: isAsync=true, parameterExcludes=[""CancellationToken""]
- Public static methods: accessibility=""Public"", isStatic=true
- Classes with [Obsolete]: kinds=[""Class""], attributes=[""ObsoleteAttribute""]

FILTERS: All specified filters are combined with AND logic. Omit a filter to skip it. Returns symbol details with locations.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        kinds = new { type = "array", items = new { type = "string" }, description = "Optional: filter by symbol kinds (can specify multiple). For types: Class, Interface, Struct, Enum, Delegate. For members: Method, Property, Field, Event. Example: ['Class', 'Interface']" },
                        isAsync = new { type = "boolean", description = "Optional: filter methods by async/await (true for async methods, false for sync methods)" },
                        namespaceFilter = new { type = "string", description = "Optional: filter by namespace (supports wildcards). Examples: 'MyApp.Core.*', '*.Services'" },
                        accessibility = new { type = "string", description = "Optional: filter by accessibility. Values: Public, Private, Internal, Protected, ProtectedInternal, PrivateProtected" },
                        isStatic = new { type = "boolean", description = "Optional: filter by static modifier (true for static, false for instance)" },
                        type = new { type = "string", description = "Optional: filter fields/properties by their type. Partial match. Example: 'ILogger' finds all ILogger fields/properties" },
                        returnType = new { type = "string", description = "Optional: filter methods by return type. Partial match. Example: 'Task' finds all methods returning Task" },
                        attributes = new { type = "array", items = new { type = "string" }, description = "Optional: filter by attributes (must have ALL specified). Example: ['ObsoleteAttribute', 'EditorBrowsableAttribute']" },
                        parameterIncludes = new { type = "array", items = new { type = "string" }, description = "Optional: filter methods that MUST have these parameter types (partial match). Example: ['CancellationToken']" },
                        parameterExcludes = new { type = "array", items = new { type = "string" }, description = "Optional: filter methods that must NOT have these parameter types (partial match). Example: ['CancellationToken']" },
                        maxResults = new { type = "integer", description = "Maximum number of results (default: 100)" }
                    },
                    required = new string[] { }
                }
            },
            new() {
                Name = "roslyn_get_diagnostics",
                Description = "Get compiler errors, warnings, and info messages for a file or entire project. Returns: list of diagnostics with id, message, severity, and location. Use before committing to catch issues.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Optional: path to specific file, omit for all files" },
                        projectPath = new { type = "string", description = "Optional: path to specific project" },
                        severity = new { type = "string", description = "Optional: filter by severity (Error, Warning, Info)" },
                        includeHidden = new { type = "boolean", description = "Include hidden diagnostics (default: false)" }
                    }
                }
            },
            new() {
                Name = "roslyn_get_code_fixes",
                Description = "Get available code fixes for a specific diagnostic. Returns list of fix titles and descriptions. WORKFLOW: (1) get_diagnostics to find issues, (2) get_code_fixes to see options, (3) apply_code_fix to fix. Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        diagnosticId = new { type = "string", description = "Diagnostic ID (e.g., CS0246)" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" }
                    },
                    required = new[] { "filePath", "diagnosticId", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_apply_code_fix",
                Description = "Apply automated code fix for a diagnostic. WORKFLOW: (1) Call with no fixIndex to list available fixes, (2) Call with fixIndex and preview=true to preview changes, (3) Call with preview=false to apply. IMPORTANT: Uses ZERO-BASED coordinates. Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        diagnosticId = new { type = "string", description = "Diagnostic ID (e.g., CS0168, CS1998, CS4012)" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" },
                        fixIndex = new { type = "integer", description = "Index of fix to apply (omit to list available fixes). Call without this parameter first to see available fixes." },
                        preview = new { type = "boolean", description = "Preview mode (default: true). Set to false to apply changes to disk. ALWAYS preview first!" }
                    },
                    required = new[] { "filePath", "diagnosticId", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_get_project_structure",
                Description = "Get solution/project structure. IMPORTANT: For large solutions (100+ projects), use summaryOnly=true or projectNamePattern to avoid token limit errors. Maximum output is limited to 25,000 tokens.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        includeReferences = new { type = "boolean", description = "Include package references (default: true, limited to 100 per project)" },
                        includeDocuments = new { type = "boolean", description = "Include document lists (default: false, limited to 500 per project)" },
                        projectNamePattern = new { type = "string", description = "Filter projects by name pattern (supports * and ? wildcards, e.g., '*.Application' or 'MyApp.*')" },
                        maxProjects = new { type = "integer", description = "Maximum number of projects to return (e.g., 10 for large solutions)" },
                        summaryOnly = new { type = "boolean", description = "Return only project names and counts (default: false, recommended for large solutions)" }
                    }
                }
            },
            new() {
                Name = "roslyn_organize_usings",
                Description = "Sort and remove unused using directives in a file. Returns the modified file content. Automatically removes unused usings and sorts alphabetically. Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" }
                    },
                    required = new[] { "filePath" }
                }
            },
            new() {
                Name = "roslyn_organize_usings_batch",
                Description = "Organize using directives for multiple files in a project. Supports file pattern filtering (e.g., '*.cs', 'Services/*.cs'). PREVIEW mode by default - set preview=false to apply changes. Note: VB.NET files are skipped automatically.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Optional: Project name to process. If omitted, processes all projects in solution." },
                        filePattern = new { type = "string", description = "Optional: Glob pattern to filter files (e.g., '*.cs', 'Services/*.cs', '*Repository.cs'). Matches against file names, not full paths." },
                        preview = new { type = "boolean", description = "Preview mode (default: true). Set to false to apply changes to disk. ALWAYS preview first!" }
                    },
                    required = new string[] { }
                }
            },
            new() {
                Name = "roslyn_format_document_batch",
                Description = "Format multiple documents in a project using Roslyn's NormalizeWhitespace. Supports C# and VB.NET files. Ensures consistent indentation, spacing, and line breaks. PREVIEW mode by default - set preview=false to apply changes.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Optional: Project name to format. If omitted, formats all projects in solution." },
                        includeTests = new { type = "boolean", description = "Include test projects (default: true). Set to false to skip projects with 'Test' in the name." },
                        preview = new { type = "boolean", description = "Preview mode (default: true). Set to false to apply changes to disk. ALWAYS preview first!" }
                    },
                    required = new string[] { }
                }
            },
            new() {
                Name = "roslyn_get_method_overloads",
                Description = "Get all overloads of a method. Returns list of signatures with parameter details. Use when you need to choose between overloads. IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_get_containing_member",
                Description = "Get information about the containing method/property/class at a position. Returns the enclosing symbol's name, kind, and signature. Useful for understanding context. IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_find_callers",
                Description = "Find all methods/properties that call or reference a specific symbol (inverse of find_references). Essential for impact analysis: 'If I change this method, what code will be affected?' IMPORTANT: Uses ZERO-BASED coordinates.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" },
                        maxResults = new { type = "integer", description = "Maximum number of call sites to return (default: 100). Results are truncated with a hint if limit is exceeded." }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_find_unused_code",
                Description = @"Find unused types, methods, properties, and fields in a project or entire solution. Returns symbols with zero references (excluding their declaration).

USAGE: find_unused_code() for entire solution, or find_unused_code(projectName=""MyProject"") for specific project.
OUTPUT: List of unused symbols with location, kind, and accessibility. Default limit: 50 results.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Optional: analyze specific project by name, omit to analyze entire solution" },
                        includePrivate = new { type = "boolean", description = "Include private members (default: true)" },
                        includeInternal = new { type = "boolean", description = "Include internal members (default: false - usually want to keep internal APIs)" },
                        symbolKindFilter = new { type = "string", description = "Optional: filter by symbol kind (Class, Method, Property, Field)" },
                        maxResults = new { type = "integer", description = "Maximum results to return (default: 50, helps manage large outputs)" }
                    }
                }
            },
            new() {
                Name = "roslyn_rename_symbol",
                Description = "Safely rename a symbol (type, method, property, etc.) across the entire solution. Uses Roslyn's semantic analysis to ensure all references are updated. SUPPORTS PREVIEW MODE - always preview first! IMPORTANT: Uses ZERO-BASED coordinates. Default shows first 20 files with summary verbosity.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file containing the symbol" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" },
                        newName = new { type = "string", description = "New name for the symbol" },
                        preview = new { type = "boolean", description = "Preview changes without applying (default: true). ALWAYS preview first!" },
                        maxFiles = new { type = "integer", description = "Max files to show in preview (default: 20, prevents large outputs)" },
                        verbosity = new { type = "string", description = "Output detail level: 'summary' (default, file paths + counts only ~200 tokens/file), 'compact' (add locations ~500 tokens/file), 'full' (include old/new text ~3000+ tokens/file)" }
                    },
                    required = new[] { "filePath", "line", "column", "newName" }
                }
            },
            new() {
                Name = "roslyn_extract_interface",
                Description = @"Generate an interface from a class or struct. Extracts all public instance members (methods, properties, events).

USAGE: Position on class declaration, provide interfaceName=""IMyService"".
OUTPUT: Generated interface code ready to insert. Useful for dependency injection and testability.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file containing the class" },
                        line = new { type = "integer", description = "Zero-based line number (editor line - 1)" },
                        column = new { type = "integer", description = "Zero-based column number (editor column - 1)" },
                        interfaceName = new { type = "string", description = "Name for the new interface (e.g., 'IMyService')" },
                        includeMemberNames = new { type = "array", items = new { type = "string" }, description = "Optional: specific member names to include (omit to include all public members)" }
                    },
                    required = new[] { "filePath", "line", "column", "interfaceName" }
                }
            },
            new() {
                Name = "roslyn_dependency_graph",
                Description = @"Visualize project dependencies as a graph. Shows which projects reference which, detects circular dependencies.

OUTPUT: format=""json"" returns structured data with nodes/edges. format=""mermaid"" returns diagram syntax.
USE CASE: Understand solution architecture, find circular dependencies, plan refactoring.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        format = new { type = "string", description = "Output format: 'json' (default) returns structured data, 'mermaid' returns Mermaid diagram syntax" }
                    }
                }
            },

            // ============ NEW TOOLS: Name-Based Type Discovery ============

            new() {
                Name = "roslyn_get_type_members",
                Description = @"Get all members (methods, properties, fields, events) of a type BY NAME.

USAGE PATTERNS:
- Basic: get_type_members(""MyClass"") - list all members
- With inheritance: get_type_members(""MyService"", includeInherited=true)
- Filter by kind: get_type_members(""MyClass"", memberKind=""Method"")
- Verbosity control: verbosity=""summary"" (names only), ""compact"" (default, + signatures), ""full"" (+ docs, attrs)

WORKS WITH: Fully-qualified (""MyNamespace.MyClass""), simple (""MyClass""), or partial names.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new { type = "string", description = "Type name (e.g., 'MyClass', 'MyNamespace.MyService')" },
                        includeInherited = new { type = "boolean", description = "Include members from base classes (default: false)" },
                        memberKind = new { type = "string", description = "Filter: 'Method', 'Property', 'Field', 'Event'" },
                        verbosity = new { type = "string", description = "'summary' (names only), 'compact' (default), 'full' (+ docs, attrs)" },
                        maxResults = new { type = "integer", description = "Maximum members to return (default: 100)" }
                    },
                    required = new[] { "typeName" }
                }
            },
            new() {
                Name = "roslyn_get_method_signature",
                Description = @"Get detailed method signature BY NAME including parameters, return type, nullability, and modifiers.

USAGE: get_method_signature(""MyClass"", ""ProcessData"") or with overload selection: get_method_signature(""MyClass"", ""ProcessData"", overloadIndex=1)",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new { type = "string", description = "Containing type name" },
                        methodName = new { type = "string", description = "Method name" },
                        overloadIndex = new { type = "integer", description = "Which overload (0-based, default: 0)" }
                    },
                    required = new[] { "typeName", "methodName" }
                }
            },
            new() {
                Name = "roslyn_get_attributes",
                Description = @"Find all symbols with specific attributes.

USAGE:
- Find obsolete: get_attributes(""Obsolete"")
- Find serializable: get_attributes(""Serializable"")
- Scope to project: get_attributes(""Obsolete"", scope=""project:MyProject"")
- Scope to file: get_attributes(""Obsolete"", scope=""file:MyClass.cs"")",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        attributeName = new { type = "string", description = "Attribute name (e.g., 'Obsolete', 'Serializable', 'JsonProperty')" },
                        scope = new { type = "string", description = "'solution' (default), 'project:Name', or 'file:path'" },
                        maxResults = new { type = "integer", description = "Maximum results (default: 100)" }
                    },
                    required = new[] { "attributeName" }
                }
            },
            new() {
                Name = "roslyn_get_derived_types",
                Description = @"Find all types inheriting from a base type BY NAME.

USAGE:
- Find all subclasses: get_derived_types(""BaseService"")
- Direct children only: get_derived_types(""BaseClass"", includeTransitive=false)",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        baseTypeName = new { type = "string", description = "Base type name" },
                        includeTransitive = new { type = "boolean", description = "Include indirect descendants (default: true)" },
                        maxResults = new { type = "integer", description = "Maximum results (default: 100)" }
                    },
                    required = new[] { "baseTypeName" }
                }
            },
            new() {
                Name = "roslyn_get_base_types",
                Description = @"Get full inheritance chain BY NAME.

USAGE: get_base_types(""MyService"") returns: MyService ? BaseService ? ... ? Object",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new { type = "string", description = "Type name" }
                    },
                    required = new[] { "typeName" }
                }
            },
            new() {
                Name = "roslyn_analyze_data_flow",
                Description = @"Analyze variable assignments and usage in a code region.

Returns: variablesDeclared, alwaysAssigned, dataFlowsIn/Out, readInside/Outside, writtenInside/Outside, captured.

USAGE: analyze_data_flow(""path/to/file.cs"", startLine=10, endLine=25)
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        startLine = new { type = "integer", description = "Start line (0-based)" },
                        endLine = new { type = "integer", description = "End line (0-based)" }
                    },
                    required = new[] { "filePath", "startLine", "endLine" }
                }
            },
            new() {
                Name = "roslyn_analyze_control_flow",
                Description = @"Analyze branching and reachability in a code region.

Returns: entryPoints, exitPoints, returnStatements, endPointIsReachable.

USAGE: analyze_control_flow(""path/to/file.cs"", startLine=10, endLine=25)
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        startLine = new { type = "integer", description = "Start line (0-based)" },
                        endLine = new { type = "integer", description = "End line (0-based)" }
                    },
                    required = new[] { "filePath", "startLine", "endLine" }
                }
            },

            // ============ COMPOUND TOOLS ============

            new() {
                Name = "roslyn_get_type_overview",
                Description = @"Get comprehensive type overview in ONE CALL: type info + base types (first 3) + member counts.

USAGE: get_type_overview(""MyService"") - returns everything you need to understand a type quickly.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new { type = "string", description = "Type name" }
                    },
                    required = new[] { "typeName" }
                }
            },
            new() {
                Name = "roslyn_analyze_method",
                Description = @"Get comprehensive method analysis in ONE CALL: signature + callers + outgoing calls + location.

USAGE: analyze_method(""MyService"", ""ProcessData"") or analyze_method(""MyClass"", ""Calculate"", includeCallers=true, includeOutgoingCalls=true)",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new { type = "string", description = "Containing type name" },
                        methodName = new { type = "string", description = "Method name" },
                        includeCallers = new { type = "boolean", description = "Include caller analysis (default: true)" },
                        includeOutgoingCalls = new { type = "boolean", description = "Include methods/properties this method calls (default: false)" },
                        maxCallers = new { type = "integer", description = "Max callers to return (default: 20)" },
                        maxOutgoingCalls = new { type = "integer", description = "Max outgoing calls to return (default: 50)" }
                    },
                    required = new[] { "typeName", "methodName" }
                }
            },
            new() {
                Name = "roslyn_get_file_overview",
                Description = @"Get comprehensive file overview in ONE CALL: diagnostics summary + type declarations + namespace + line count.

USAGE: get_file_overview(""path/to/MyClass.cs"")",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" }
                    },
                    required = new[] { "filePath" }
                }
            },

            // Phase 2: AI-Focused Tools
            new() {
                Name = "roslyn_get_missing_members",
                Description = @"Get all interface and abstract members that must be implemented for a type.

USAGE: Position on a class that implements interfaces or extends abstract classes.
OUTPUT: List of missing members with exact signatures ready to copy. Use before implementing interfaces.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number on the type declaration" },
                        column = new { type = "integer", description = "Zero-based column number" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_get_outgoing_calls",
                Description = "Get all methods and properties that a method calls. Helps understand method dependencies and behavior. Returns list of called symbols with locations. IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number inside the method" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        maxDepth = new { type = "integer", description = "How deep to trace calls (1 = direct only, default: 1)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_validate_code",
                Description = @"Check if code would compile without writing to disk. Supports C# and VB.NET. Use to validate generated code before applying.

USAGE: validate_code(code=""public void Foo() {}"", contextFilePath=""path/to/file.cs"") to check with existing usings/namespace.
For VB.NET: pass a .vb contextFilePath, or set language=""vbnet"" when no context file is available.
OUTPUT: compiles (bool), errors list with line numbers. Essential before inserting AI-generated code.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        code = new { type = "string", description = "Code snippet to validate (C# or VB.NET)" },
                        contextFilePath = new { type = "string", description = "Optional: file to use for context (usings/imports, namespace). Language inferred from file extension." },
                        standalone = new { type = "boolean", description = "If true, treat code as complete file (default: false)" },
                        language = new { type = "string", description = "Language hint when no contextFilePath: 'csharp' (default) or 'vbnet'" }
                    },
                    required = new[] { "code" }
                }
            },
            new() {
                Name = "roslyn_check_type_compatibility",
                Description = @"Check if one type can be assigned to another. Use before generating assignments or casts.

USAGE: check_type_compatibility(sourceType=""MyDerivedClass"", targetType=""MyBaseClass"")
OUTPUT: compatible (bool), requiresCast (bool), conversionKind, and explanation of why/why not.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        sourceType = new { type = "string", description = "The source type name (e.g., 'MyDerivedClass')" },
                        targetType = new { type = "string", description = "The target type name (e.g., 'MyBaseClass')" }
                    },
                    required = new[] { "sourceType", "targetType" }
                }
            },
            new() {
                Name = "roslyn_get_instantiation_options",
                Description = @"Get all ways to create an instance of a type: constructors, factory methods, and builder patterns.

USAGE: get_instantiation_options(typeName=""HttpClient"")
OUTPUT: List of constructors with signatures, static factory methods, and hints (e.g., ""implements IDisposable"").",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new { type = "string", description = "The type name to check (e.g., 'HttpClient')" }
                    },
                    required = new[] { "typeName" }
                }
            },
            new() {
                Name = "roslyn_analyze_change_impact",
                Description = @"Analyze what would break if you change a symbol. Identifies breaking changes before you make them.

USAGE: analyze_change_impact(filePath, line, column, changeType=""rename|changeType|addParameter|removeParameter"")
OUTPUT: List of impacted locations, whether change is safe, and specific issues at each location.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number of the symbol" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        changeType = new { type = "string", description = "Type of change: rename, changeType, addParameter, removeParameter, changeAccessibility, delete" },
                        newValue = new { type = "string", description = "Optional: new value for rename/changeType" }
                    },
                    required = new[] { "filePath", "line", "column", "changeType" }
                }
            },
            new() {
                Name = "roslyn_get_method_source",
                Description = @"Get the actual source code of a method by type and method name. Eliminates need for file Read.

USAGE: get_method_source(typeName=""MyService"", methodName=""ProcessData"")
OUTPUT: Full method source including signature, body, location (file + line numbers), and line count.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new { type = "string", description = "The containing type name (e.g., 'MyService', 'MyController')" },
                        methodName = new { type = "string", description = "The method name (e.g., 'ProcessData')" },
                        overloadIndex = new { type = "integer", description = "Which overload to get (0-based, default: 0)" }
                    },
                    required = new[] { "typeName", "methodName" }
                }
            },
            new() {
                Name = "roslyn_get_method_source_batch",
                Description = @"Get source code for multiple methods in a single call (batch optimization).

USAGE: get_method_source_batch(methods: [{typeName: 'ServiceA', methodName: 'Process'}, {typeName: 'ServiceB', methodName: 'Handle'}])
OUTPUT: Results array with source for each method, plus errors array for any that failed.
BENEFIT: One call instead of multiple - reduces round trips when tracing code flows.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        methods = new
                        {
                            type = "array",
                            description = "Array of method requests",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    typeName = new { type = "string", description = "Containing type name" },
                                    methodName = new { type = "string", description = "Method name" },
                                    overloadIndex = new { type = "integer", description = "Which overload (0-based, optional)" }
                                },
                                required = new[] { "typeName", "methodName" }
                            }
                        },
                        maxMethods = new { type = "integer", description = "Maximum methods to process (default: 20)" }
                    },
                    required = new[] { "methods" }
                }
            },
            new() {
                Name = "roslyn_generate_constructor",
                Description = @"Generate a constructor from fields and/or properties of a type.

USAGE: Position on class/struct declaration. Use includeProperties=true for auto-properties.
OUTPUT: constructorCode string ready to paste, parameter list, and field assignments.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file containing the type" },
                        line = new { type = "integer", description = "Zero-based line number on the type declaration" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        includeProperties = new { type = "boolean", description = "Include properties with setters (default: false)" },
                        initializeToDefault = new { type = "boolean", description = "Use ?? default for nullable types (default: false)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_change_signature",
                Description = @"Change a method signature and preview impact on all call sites.

ACTIONS: add (new param), remove, rename, reorder parameters.
WORKFLOW: (1) Call with preview=true (default) to see affected call sites, (2) Review changes, (3) Call with preview=false to apply.
OUTPUT: oldSignature, newSignature, list of call sites needing updates.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file containing the method" },
                        line = new { type = "integer", description = "Zero-based line number on the method" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        changes = new
                        {
                            type = "array",
                            description = "Array of changes to apply",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    action = new { type = "string", description = "Action: 'add', 'remove', 'rename', or 'reorder'" },
                                    name = new { type = "string", description = "Parameter name (for add/remove/rename)" },
                                    type = new { type = "string", description = "Parameter type (for add)" },
                                    newName = new { type = "string", description = "New name (for rename)" },
                                    defaultValue = new { type = "string", description = "Default value (for add)" },
                                    position = new { type = "integer", description = "Position to insert (for add), -1 means end" },
                                    order = new { type = "array", items = new { type = "string" }, description = "New parameter order (for reorder)" }
                                }
                            }
                        },
                        preview = new { type = "boolean", description = "Preview mode (default: true). Set to false to apply changes." }
                    },
                    required = new[] { "filePath", "line", "column", "changes" }
                }
            },
            new() {
                Name = "roslyn_extract_method",
                Description = @"Extract selected statements into a new method. Uses data flow analysis to determine parameters and return type.

USAGE: Specify startLine/endLine range containing complete statements inside a method.
OUTPUT: extractedCode (the new method), replacementCode (the call to insert), detected parameters and return type.
WORKFLOW: (1) Preview with preview=true, (2) Apply with preview=false.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        startLine = new { type = "integer", description = "Zero-based start line of selection" },
                        endLine = new { type = "integer", description = "Zero-based end line of selection" },
                        methodName = new { type = "string", description = "Name for the new method" },
                        accessibility = new { type = "string", description = "Accessibility: private, public, internal (default: private)" },
                        preview = new { type = "boolean", description = "Preview mode (default: true). Set to false to apply." }
                    },
                    required = new[] { "filePath", "startLine", "endLine", "methodName" }
                }
            },
            new() {
                Name = "roslyn_get_code_actions_at_position",
                Description = @"Get ALL available code actions (fixes + refactorings) at a position. This is the master tool that exposes 100+ Roslyn refactorings.

USAGE: get_code_actions_at_position(filePath, line, column) or with selection: add endLine, endColumn
OUTPUT: List of actions with title, kind (fix/refactoring), equivalenceKey
WORKFLOW: (1) Call this to see available actions, (2) Use apply_code_action_by_title to apply one
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        endLine = new { type = "integer", description = "Optional: end line for selection" },
                        endColumn = new { type = "integer", description = "Optional: end column for selection" },
                        includeCodeFixes = new { type = "boolean", description = "Include fixes for diagnostics (default: true)" },
                        includeRefactorings = new { type = "boolean", description = "Include refactorings (default: true)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_apply_code_action_by_title",
                Description = @"Apply a code action by its title. Supports exact and partial matching.

USAGE: apply_code_action_by_title(filePath, line, column, title)
OUTPUT: Changed files with preview or applied changes
WORKFLOW: (1) Call get_code_actions_at_position first, (2) Apply with preview=true, (3) Apply with preview=false
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        title = new { type = "string", description = "Action title (exact or partial match)" },
                        endLine = new { type = "integer", description = "Optional: end line for selection" },
                        endColumn = new { type = "integer", description = "Optional: end column for selection" },
                        preview = new { type = "boolean", description = "Preview mode (default: true). Set to false to apply." }
                    },
                    required = new[] { "filePath", "line", "column", "title" }
                }
            },
            new() {
                Name = "roslyn_implement_missing_members",
                Description = @"Generate stub implementations for interface/abstract members.

USAGE: Position cursor on class declaration that implements interface or extends abstract class.
OUTPUT: Generated stub code for all missing members.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number on the class declaration" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        preview = new { type = "boolean", description = "Preview mode (default: true)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_encapsulate_field",
                Description = @"Convert a field to a property with getter/setter.

USAGE: Position cursor on a field declaration.
OUTPUT: Generated property wrapping the field.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number on the field" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        preview = new { type = "boolean", description = "Preview mode (default: true)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_inline_variable",
                Description = @"Inline a variable, replacing all usages with its value.

USAGE: Position cursor on a variable declaration or usage.
OUTPUT: Variable removed and all usages replaced with the expression.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        preview = new { type = "boolean", description = "Preview mode (default: true)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_extract_variable",
                Description = @"Extract an expression to a local variable.

USAGE: Position cursor on or select an expression.
OUTPUT: Expression extracted to a new local variable.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        endLine = new { type = "integer", description = "Optional: end line for selection" },
                        endColumn = new { type = "integer", description = "Optional: end column for selection" },
                        preview = new { type = "boolean", description = "Preview mode (default: true)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_get_complexity_metrics",
                Description = @"Get complexity metrics for a method or entire file.

METRICS: cyclomatic (decision points), nesting (max depth), loc (lines), parameters (count), cognitive (Sonar-style)
USAGE: get_complexity_metrics(filePath) for file, or add line/column for specific method
OUTPUT: Per-method breakdown with all requested metrics
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Optional: zero-based line for specific method" },
                        column = new { type = "integer", description = "Optional: zero-based column" },
                        metrics = new { type = "array", items = new { type = "string" }, description = "Optional: specific metrics [cyclomatic, nesting, loc, parameters, cognitive]" }
                    },
                    required = new[] { "filePath" }
                }
            },
            new() {
                Name = "roslyn_add_null_checks",
                Description = @"Add ArgumentNullException.ThrowIfNull guard clauses for nullable parameters.

USAGE: Position cursor on a method with reference type parameters.
OUTPUT: Generated guard clauses inserted at method start.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number on the method" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        preview = new { type = "boolean", description = "Preview mode (default: true)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_generate_equality_members",
                Description = @"Generate Equals, GetHashCode, and == / != operators for a type.

USAGE: Position cursor on a class or struct declaration.
OUTPUT: Generated equality members comparing all instance fields and properties.
IMPORTANT: Uses ZERO-BASED coordinates (editor line - 1).
Note: VB.NET files are not supported for this operation.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Absolute path to source file" },
                        line = new { type = "integer", description = "Zero-based line number on the type" },
                        column = new { type = "integer", description = "Zero-based column number" },
                        includeOperators = new { type = "boolean", description = "Include == and != operators (default: true)" },
                        preview = new { type = "boolean", description = "Preview mode (default: true)" }
                    },
                    required = new[] { "filePath", "line", "column" }
                }
            },
            new() {
                Name = "roslyn_get_type_members_batch",
                Description = @"Get members for multiple types in a single call (batch optimization).

USAGE: get_type_members_batch(typeNames: ['ServiceA', 'ServiceB', 'ControllerC'])
OUTPUT: Results for each type with members, or error if type not found
BENEFIT: One call instead of multiple - reduces context usage for AI agents",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeNames = new { type = "array", items = new { type = "string" }, description = "Array of type names to look up" },
                        includeInherited = new { type = "boolean", description = "Include inherited members (default: false)" },
                        memberKind = new { type = "string", description = "Filter: 'Method', 'Property', 'Field', 'Event'" },
                        verbosity = new { type = "string", description = "'summary', 'compact' (default), or 'full'" },
                        maxResultsPerType = new { type = "integer", description = "Max members per type (default: 50)" }
                    },
                    required = new[] { "typeNames" }
                }
            }
            ,
            new() {
                Name = "roslyn_find_attribute_usages",
                Description = @"Find all types and members decorated with a specific attribute.

USAGE: find_attribute_usages(attributeName: ""Authorize"")
USAGE: find_attribute_usages(attributeName: ""HttpGet"", projectName: ""MyApi"")

OUTPUT: List of symbols with the attribute, their kind, arguments, and source location.
Use for: finding all API endpoints, authorization points, serialization config, test fixtures.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        attributeName = new { type = "string", description = "Attribute name (e.g. 'Authorize', 'HttpGet', 'Obsolete')" },
                        projectName = new { type = "string", description = "Filter to specific project" },
                        maxResults = new { type = "integer", description = "Maximum results (default: 100)" }
                    },
                    required = new[] { "attributeName" }
                }
            },
            new() {
                Name = "roslyn_get_di_registrations",
                Description = @"Scan for dependency injection service registrations (AddScoped, AddTransient, AddSingleton, etc.).

USAGE: get_di_registrations()
USAGE: get_di_registrations(projectName: ""MyApi"")

OUTPUT: List of DI registrations with lifetime, service type, implementation type, and location.
Use for: understanding service wiring, finding missing registrations, auditing lifetimes.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Filter to specific project" }
                    }
                }
            },
            new() {
                Name = "roslyn_find_reflection_usage",
                Description = @"Detect dynamic/reflection-based type and method usage that is invisible to static reference searches.

USAGE: find_reflection_usage()
USAGE: find_reflection_usage(projectName: ""MyApp"", maxResults: 50)

OUTPUT: List of reflection API calls with the API used, context, and location.
Use for: finding hidden dependencies before refactoring, security audits, understanding dynamic behavior.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Filter to specific project" },
                        maxResults = new { type = "integer", description = "Maximum results (default: 100)" }
                    }
                }
            },
            new() {
                Name = "roslyn_find_circular_dependencies",
                Description = @"Detect cycles in project or namespace dependency graphs.

USAGE: find_circular_dependencies() � project-level cycles
USAGE: find_circular_dependencies(level: ""namespace"") � namespace-level cycles

OUTPUT: Dependency graph with any detected cycles listed.
Use for: architecture analysis, identifying tightly coupled components.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        level = new { type = "string", description = "'project' (default) or 'namespace'" }
                    }
                }
            },
            new() {
                Name = "roslyn_get_nuget_dependencies",
                Description = @"List NuGet package references per project with versions.

USAGE: get_nuget_dependencies()
USAGE: get_nuget_dependencies(projectName: ""MyApp"")

OUTPUT: List of projects with their NuGet packages, versions, and asset settings.
Use for: dependency audits, version checks, understanding external dependencies.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Filter to specific project" }
                    }
                }
            },
            new() {
                Name = "roslyn_get_source_generators",
                Description = @"List active source generators and their generated output per project.

USAGE: get_source_generators()
USAGE: get_source_generators(projectName: ""MyApp"")

OUTPUT: List of generators with their assembly info and generated files.
Use for: understanding generated code, debugging generator issues.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Filter to specific project" }
                    }
                }
            },
            new() {
                Name = "roslyn_get_generated_code",
                Description = @"View the source code produced by a source generator.

USAGE: get_generated_code(projectName: ""MyApp"", generatedFileName: ""MyType.g.cs"")

OUTPUT: Full source code of the generated file.
Use get_source_generators first to discover available generated files.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectName = new { type = "string", description = "Project containing the generated file" },
                        generatedFileName = new { type = "string", description = "Name of the generated file" }
                    },
                    required = new[] { "projectName", "generatedFileName" }
                }
            },

            // ============ QUALITY ANALYSIS TOOLS ============

            new() {
                Name = "roslyn_find_large_classes",
                Description = @"Find types that exceed member-count or line-count thresholds — a proxy for god-class smells.

USAGE: find_large_classes()  — uses defaults (20 members OR 500 lines)
USAGE: find_large_classes(memberCountThreshold: 30, projectFilter: ""MyApp"")

OUTPUT: Sorted list of types with memberCount, lineCount, isPartial, and location.
Use for: identifying refactoring candidates, understanding technical debt hotspots.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        memberCountThreshold = new { type = "integer", description = "Flag types with at least this many non-implicit members (default: 20)" },
                        lineCountThreshold = new { type = "integer", description = "Flag types spanning at least this many lines (default: 500)" },
                        projectFilter = new { type = "string", description = "Limit to a specific project name" }
                    }
                }
            },
            new() {
                Name = "roslyn_get_public_api_surface",
                Description = @"Enumerate all public/protected types and their members across the solution — the API surface.

USAGE: get_public_api_surface()
USAGE: get_public_api_surface(includeInternal: true, projectFilter: ""MyLib"")

OUTPUT: Projects → namespaces → types → members grouped tree.
Use for: API audits, documentation generation, comparing surfaces across versions.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectFilter = new { type = "string", description = "Limit to a specific project name" },
                        includeInternal = new { type = "boolean", description = "Include internal members in addition to public/protected (default: false)" }
                    }
                }
            },
            new() {
                Name = "roslyn_get_operators",
                Description = @"List all user-defined operators and conversion operators on a type.

USAGE: get_operators(typeName: ""Money"")

OUTPUT: Operators with their symbol (+, -, ==, implicit/explicit), return type, and parameters.
Use for: understanding custom value types, numeric types, or types with domain-specific equality.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new { type = "string", description = "Type name to inspect (e.g. 'Money', 'System.Decimal')" }
                    },
                    required = new[] { "typeName" }
                }
            },
            new() {
                Name = "roslyn_find_obsolete_usage",
                Description = @"Find every call site that invokes an [Obsolete]-decorated symbol.

USAGE: find_obsolete_usage()
USAGE: find_obsolete_usage(projectFilter: ""MyApp"", maxResults: 50)

OUTPUT: Usages grouped by error/warning severity with the obsolete symbol, its message, and location.
Use for: migration planning, upgrade audits, removing deprecated API usage.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectFilter = new { type = "string", description = "Limit to a specific project name" },
                        maxResults = new { type = "integer", description = "Maximum results (default: 100)" }
                    }
                }
            },
            new() {
                Name = "roslyn_get_call_graph",
                Description = @"Build a depth-bounded call graph (callers or callees) for a method.

USAGE: get_call_graph(typeName: ""OrderService"", methodName: ""PlaceOrder"")
USAGE: get_call_graph(typeName: ""OrderService"", methodName: ""PlaceOrder"", depth: 2, direction: ""callers"")

DIRECTIONS: 'callees' (default) — what this method calls; 'callers' — who calls this; 'both' — bidirectional
OUTPUT: { nodes, edges, cycleNodes } — graph structure for visualization or analysis.
Cycle detection prevents infinite recursion; cycle nodes are listed separately.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new { type = "string", description = "Containing type name" },
                        methodName = new { type = "string", description = "Method name" },
                        depth = new { type = "integer", description = "Maximum traversal depth 1-10 (default: 3)" },
                        direction = new { type = "string", description = "'callees' (default), 'callers', or 'both'" }
                    },
                    required = new[] { "typeName", "methodName" }
                }
            },
            new() {
                Name = "roslyn_find_async_violations",
                Description = @"Detect async/await anti-patterns: async void, Task.Result/.Wait() blocking, and .GetResult() calls.

USAGE: find_async_violations()
USAGE: find_async_violations(projectFilter: ""MyApi"", maxResults: 50)

DETECTS: async void methods (fire-and-forget exceptions), .Result blocking (deadlock risk), .Wait() blocking, .GetResult() blocking
OUTPUT: Violations with type, description, symbol name, and location.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectFilter = new { type = "string", description = "Limit to a specific project name" },
                        maxResults = new { type = "integer", description = "Maximum results (default: 100)" }
                    }
                }
            },
            new() {
                Name = "roslyn_find_disposable_misuse",
                Description = @"Find IDisposable variables declared without 'using' — potential resource leaks.

USAGE: find_disposable_misuse()
USAGE: find_disposable_misuse(projectFilter: ""MyApp"", maxResults: 50)

DETECTS: C# locals missing 'using var' or 'using ()'; VB.NET locals outside Using blocks.
OUTPUT: Violations with variable name, type name, and location.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectFilter = new { type = "string", description = "Limit to a specific project name" },
                        maxResults = new { type = "integer", description = "Maximum results (default: 100)" }
                    }
                }
            },
            new() {
                Name = "roslyn_find_event_subscribers",
                Description = @"Find all += / -= event subscriptions (C#) and AddHandler / RemoveHandler statements (VB.NET).

USAGE: find_event_subscribers()  — all event wiring in the solution
USAGE: find_event_subscribers(typeName: ""Button"", eventName: ""Clicked"")

OUTPUT: Subscribers with event FQN, subscribe/unsubscribe kind, handler kind (lambda/method reference), and location.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        typeName = new { type = "string", description = "Filter to events on this type (optional)" },
                        eventName = new { type = "string", description = "Filter to a specific event name (optional)" },
                        projectFilter = new { type = "string", description = "Limit to a specific project name" },
                        maxResults = new { type = "integer", description = "Maximum results (default: 100)" }
                    }
                }
            },
            new() {
                Name = "roslyn_find_naming_violations",
                Description = @"Check C# naming conventions: interfaces start with 'I', types/methods/properties PascalCase, parameters camelCase, private fields prefixed '_'.

USAGE: find_naming_violations()
USAGE: find_naming_violations(projectFilter: ""MyApp"", maxResults: 50)

OUTPUT: Violations with rule type, description, symbol name, and location.
Note: C# projects only — VB.NET is skipped (VB conventions differ).",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        projectFilter = new { type = "string", description = "Limit to a specific project name" },
                        maxResults = new { type = "integer", description = "Maximum results (default: 100)" }
                    }
                }
            },
            new() {
                Name = "roslyn_find_god_objects",
                Description = @"Find classes that are both large (many members or lines) AND highly coupled (reference many external namespaces) — the classic god object smell.

USAGE: find_god_objects()
USAGE: find_god_objects(memberThreshold: 15, outgoingNamespaceThreshold: 4)

OUTPUT: Types with memberCount, lineCount, outgoingNamespaceCount, and the namespaces they reference.
Note: C# projects only.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        memberThreshold = new { type = "integer", description = "Minimum member count to consider (default: 20)" },
                        lineThreshold = new { type = "integer", description = "Minimum line count to consider (default: 500)" },
                        outgoingNamespaceThreshold = new { type = "integer", description = "Minimum distinct external namespaces referenced (default: 5)" },
                        projectFilter = new { type = "string", description = "Limit to a specific project name" }
                    }
                }
            },
            new() {
                Name = "roslyn_peek_il",
                Description = @"Disassemble a method from a referenced (metadata-only) assembly to MSIL bytecode.

USAGE: peek_il(methodSymbol: ""Newtonsoft.Json.JsonConvert.SerializeObject(object)"")
USAGE: peek_il(methodSymbol: ""System.Text.Json.JsonSerializer.Serialize(object, System.Type)"")
USAGE: peek_il(methodSymbol: ""System.Collections.Generic.List`1..ctor(int)"")

OUTPUT: MSIL text, assembly name, and version.
Useful for understanding what a closed-source library method actually does.
Error if the method is in source — use go_to_definition instead.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        methodSymbol = new { type = "string", description = "Fully-qualified method name, e.g. \"Newtonsoft.Json.JsonConvert.SerializeObject(object)\"" }
                    },
                    required = new[] { "methodSymbol" }
                }
            },
            new() {
                Name = "roslyn_inspect_external_assembly",
                Description = @"Browse the public API surface of any assembly referenced by the loaded solution.

USAGE: inspect_external_assembly(assemblyName: ""FluentAssertions"", mode: ""summary"")
USAGE: inspect_external_assembly(assemblyName: ""FluentAssertions"", mode: ""namespace"", namespaceFilter: ""FluentAssertions"")

mode=""summary"": namespace tree with type counts.
mode=""namespace"": full type and member listing with XML doc summaries for a specific namespace.
Use get_nuget_dependencies first to discover assembly names.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        assemblyName    = new { type = "string", description = "Assembly name without .dll extension (e.g. \"Newtonsoft.Json\")" },
                        mode            = new { type = "string", description = "\"summary\" (default) or \"namespace\"" },
                        namespaceFilter = new { type = "string", description = "Required when mode=\"namespace\" — e.g. \"Newtonsoft.Json\"" }
                    },
                    required = new[] { "assemblyName" }
                }
            }
        ];

    internal static IReadOnlyList<ToolDescriptor> GetTools() => Tools;
}
