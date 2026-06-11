# DotNetLensMcp

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

A Model Context Protocol (MCP) server providing **74 AI-optimized tools** for .NET semantic code analysis, navigation, refactoring, and code generation using Microsoft Roslyn.

**DotNetLensMcp** is a fork of [sharplens-mcp](https://github.com/pzalutski-pixel/sharplens-mcp) extended with **VB.NET support** and ILSpy-backed decompilation tools ported from [roslyn-codelens-mcp](https://github.com/MarcelRoozekrans/roslyn-codelens-mcp). Semantic and symbol-level tools work across both C# and VB.NET. Syntax-level refactoring and code generation remain C#-only with graceful "not supported" responses for `.vb` files.

Built for AI coding agents — provides compiler-accurate code understanding that AI cannot infer from reading source files alone.

[cs]: https://img.shields.io/badge/C%23-239120?style=flat-square&logo=csharp&logoColor=white
[vb]: https://img.shields.io/badge/VB.NET-512BD4?style=flat-square&logo=dotnet&logoColor=white

## Features

- **74 tools** — Navigation, analysis, refactoring, code generation, quality analysis, decompilation, and diagnostics
- **C# and VB.NET support** — Navigation, analysis, quality analysis, and decompilation work across both languages; code generation and most refactoring are C# only
- **Compiler-accurate analysis** — Powered by Roslyn; resolves semantic references, not text matches
- **ILSpy decompilation** — Browse IL bytecode and external assembly APIs without access to source
- **Preview mode** — Inspect refactoring changes before applying them
- **Batch operations** — Multiple lookups in one call to reduce context usage
- **Structured responses** — Consistent `success/error/data` format with `suggestedNextTools` to chain tools efficiently

## Why Use This with Claude Code?

Claude Code has native LSP support for basic navigation. DotNetLensMcp adds **deep semantic analysis**:

| Capability | Native LSP | DotNetLensMcp |
|------------|:----------:|:-------------:|
| Go to definition | ✅ | ✅ |
| Find references | ✅ | ✅ |
| VB.NET symbol navigation | ❌ | ✅ |
| Find async methods missing CancellationToken | ❌ | ✅ |
| Impact analysis (what breaks?) | ❌ | ✅ |
| Dead code detection | ❌ | ✅ |
| Complexity metrics | ❌ | ✅ |
| Safe refactoring with preview | ❌ | ✅ |
| Batch operations | ❌ | ✅ |

## Language Support

| Capability | ![C#][cs] | ![VB.NET][vb] |
|------------|:---------:|:-------------:|
| Navigation & discovery | ✅ | ✅ |
| Analysis | ✅ | ✅ (9/11 tools) |
| Refactoring | ✅ | ✅ (2/15 tools) |
| Code generation | ✅ | ❌ |
| Quality analysis | ✅ | ✅ (8/10 tools) |
| Decompilation | ✅ | ✅ |

Unsupported tools return a structured `VB_NOT_SUPPORTED` error — they never crash or return misleading output.

## Requirements

- **.NET 10.0 SDK or later** — required to build and run the MCP server
- Analyzes .NET solutions containing C# projects, VB.NET projects, or both, including projects targeting earlier supported .NET TFMs
- MCP-compatible AI agent

## Installation

```bash
git clone https://github.com/mdziadowiec/DotNetLensMCP
cd DotNetLensMCP
dotnet publish src -c Release -o ./publish
```

Run with:
```bash
dotnet publish/DotNetLensMcp.dll
```

> For the upstream NuGet/npm packages (`dotnet tool install -g SharpLensMcp` / `npx -y sharplens-mcp`), see the [original sharplens-mcp repository](https://github.com/pzalutski-pixel/sharplens-mcp). Those packages do not include VB.NET support.

## Configuration

| Environment Variable | Description | Default |
|---------------------|-------------|---------|
| `DOTNET_SOLUTION_PATH` | Path to `.sln` or `.slnx` file to auto-load on startup | None (must call `load_solution`) |
| `DOTNETLENS_ABSOLUTE_PATHS` | Use absolute paths instead of relative | `false` (relative paths save tokens) |
| `ROSLYN_LOG_LEVEL` | Logging verbosity: `Trace`, `Debug`, `Information`, `Warning`, `Error` | `Information` |
| `ROSLYN_TIMEOUT_SECONDS` | Timeout for long-running Roslyn operations (compilation, reference search); returns a structured `TIMEOUT` error when exceeded | `30` |
| `ROSLYN_MAX_DIAGNOSTICS` | Maximum diagnostics to return | `100` |
| `ROSLYN_ENABLE_SEMANTIC_CACHE` | Enable semantic model caching | `true` (set to `false` to disable) |

If `DOTNET_SOLUTION_PATH` is not set, you must call the `load_solution` tool before using other tools.

## Claude Code Setup

1. **Build and publish** (see [Installation](#installation))

2. **Create `.mcp.json` in your project root**:
```json
{
  "mcpServers": {
    "dotnetlens": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["/absolute/path/to/publish/DotNetLensMcp.dll"],
      "env": {
        "DOTNET_SOLUTION_PATH": "/path/to/your/Solution.sln"
      }
    }
  }
}
```

3. **Restart Claude Code** to load the MCP server

4. **Verify** by asking Claude to run a health check on the Roslyn server

### CLAUDE.md tip

Add to your project's `CLAUDE.md` to steer Claude toward the semantic tools:

```
For .NET code analysis (C# and VB.NET), prefer DotNetLensMcp tools over native tools:

Navigation & lookup:
- Use `roslyn_search_symbols` instead of Grep for finding types, methods, and members by name
- Use `roslyn_go_to_definition` to jump to any symbol's declaration
- Use `roslyn_get_method_source` instead of Read for viewing a method body without opening the full file
- Use `roslyn_get_type_members` to inspect all members of a type before reading its source

Impact analysis:
- Use `roslyn_find_references` for semantic (not text) references — won't miss renamed symbols
- Use `roslyn_find_callers` before changing a method signature to see all call sites
- Use `roslyn_analyze_change_impact` to predict what breaks before making a change

Code quality:
- Use `roslyn_find_async_violations` to detect async void / .Result blocking / fire-and-forget
- Use `roslyn_find_disposable_misuse` to catch IDisposable variables declared without using
- Use `roslyn_get_diagnostics` to get compiler errors/warnings for a file

After editing files with Edit or Write tools, always call `roslyn_sync_documents` to keep the
Roslyn workspace in sync before running further analysis tools.

Most tools support both C# and VB.NET. Code generation and most refactoring tools are C# only —
see the Language Support table for per-category details.
```

## Agent Responsibility: Document Synchronization

DotNetLensMcp maintains an in-memory representation of your solution. When files are modified externally (via Edit/Write tools), the agent must synchronize changes. This applies equally to `.cs` and `.vb` files.

| Action | Call sync_documents? |
|--------|---------------------|
| Used Edit tool to modify `.cs` or `.vb` files | ✅ **Yes** |
| Used Write tool to create new source files | ✅ **Yes** |
| Deleted source files | ✅ **Yes** |
| Used DotNetLensMcp refactoring tools (rename, extract, etc.) | ❌ No (auto-updated) |
| Modified `.csproj` or `.vbproj` files | ❌ No (use `load_solution` instead) |

```
# After editing specific files
sync_documents(filePaths: ["src/MyClass.cs", "src/MyModule.vb"])

# After bulk changes - sync all documents
sync_documents()
```

## Tool Reference

> ✅ full support · ❌ returns `VB_NOT_SUPPORTED` · ⚠️ VB files skipped silently (no error)

### Navigation & Discovery (17 tools)

| Tool | Description | ![C#][cs] | ![VB.NET][vb] |
|------|-------------|:---------:|:-------------:|
| `get_symbol_info` | Semantic info at position | ✅ | ✅ |
| `go_to_definition` | Jump to symbol definition | ✅ | ✅ |
| `find_references` | All references across solution | ✅ | ✅ |
| `find_implementations` | Interface/abstract implementations | ✅ | ✅ |
| `find_callers` | Impact analysis — who calls this? | ✅ | ✅ |
| `get_type_hierarchy` | Inheritance chain | ✅ | ✅ |
| `search_symbols` | Glob pattern search (`*Handler`, `Get*`) | ✅ | ✅ |
| `semantic_query` | Multi-filter search (async, public, etc.) | ✅ | ✅ |
| `get_type_members` | All members by type name | ✅ | ✅ |
| `get_type_members_batch` | Multiple types in one call | ✅ | ✅ |
| `get_method_signature` | Detailed signature by name | ✅ | ✅ |
| `get_derived_types` | Find all subclasses | ✅ | ✅ |
| `get_base_types` | Full inheritance chain | ✅ | ✅ |
| `get_attributes` | List attributes on a symbol | ✅ | ✅ |
| `get_containing_member` | Enclosing symbol at position | ✅ | ✅ |
| `get_method_overloads` | All overloads of a method | ✅ | ✅ |
| `find_attribute_usages` | Find types/members by attribute | ✅ | ✅ |

### Analysis (11 tools)

| Tool | Description | ![C#][cs] | ![VB.NET][vb] |
|------|-------------|:---------:|:-------------:|
| `get_diagnostics` | Compiler errors/warnings | ✅ | ✅ |
| `analyze_change_impact` | What breaks if changed? | ✅ | ✅ |
| `check_type_compatibility` | Can A assign to B? | ✅ | ✅ |
| `get_outgoing_calls` | What does this method call? | ✅ | ✅ |
| `find_unused_code` | Dead code detection | ✅ | ✅ |
| `get_complexity_metrics` | Cyclomatic, nesting, LOC, cognitive | ✅ | ✅ |
| `find_circular_dependencies` | Project and namespace cycle detection | ✅ | ✅ |
| `get_missing_members` | Unimplemented interface/abstract members | ✅ | ✅ |
| `analyze_data_flow` | Variable assignments and usage | ✅ | ❌ |
| `analyze_control_flow` | Branching/reachability | ✅ | ❌ |
| `validate_code` | Compile check without writing | ✅ | ✅ |

### Refactoring (15 tools)

| Tool | Description | ![C#][cs] | ![VB.NET][vb] |
|------|-------------|:---------:|:-------------:|
| `rename_symbol` | Safe rename across solution | ✅ | ✅ |
| `change_signature` | Add/remove/reorder parameters | ✅ | ❌ |
| `extract_method` | Extract with data flow analysis | ✅ | ❌ |
| `extract_interface` | Generate interface from class | ✅ | ❌ |
| `organize_usings` | Sort and remove unused | ✅ | ❌ |
| `organize_usings_batch` | Batch organize across project | ✅ | ⚠️ |
| `format_document_batch` | Batch format files in project | ✅ | ✅ |
| `get_code_actions_at_position` | All Roslyn refactorings at position | ✅ | ❌ |
| `apply_code_action_by_title` | Apply any refactoring by title | ✅ | ❌ |
| `implement_missing_members` | Generate interface stubs | ✅ | ❌ |
| `encapsulate_field` | Field to property | ✅ | ❌ |
| `inline_variable` | Inline temp variable | ✅ | ❌ |
| `extract_variable` | Extract expression to variable | ✅ | ❌ |
| `get_code_fixes` / `apply_code_fix` | Diagnostic-driven fixes | ✅ | ❌ |

### Code Generation (3 tools)

| Tool | Description | ![C#][cs] | ![VB.NET][vb] |
|------|-------------|:---------:|:-------------:|
| `add_null_checks` | Generate ArgumentNullException guards | ✅ | ❌ |
| `generate_equality_members` | Equals/GetHashCode/operators | ✅ | ❌ |
| `generate_constructor` | Generate constructor from fields/properties | ✅ | ❌ |

### Compound Tools (6 tools)

| Tool | Description | ![C#][cs] | ![VB.NET][vb] |
|------|-------------|:---------:|:-------------:|
| `get_type_overview` | Full type info in one call | ✅ | ✅ |
| `analyze_method` | Signature + callers + outgoing calls + location | ✅ | ✅ |
| `get_file_overview` | File summary with diagnostics | ✅ | ✅ |
| `get_method_source` | Source code by name | ✅ | ✅ |
| `get_method_source_batch` | Multiple method sources in one call | ✅ | ✅ |
| `get_instantiation_options` | How to create a type | ✅ | ✅ |

### Discovery (2 tools)

| Tool | Description | ![C#][cs] | ![VB.NET][vb] |
|------|-------------|:---------:|:-------------:|
| `get_di_registrations` | Scan DI service registrations | ✅ | ⚠️ |
| `find_reflection_usage` | Detect reflection/dynamic usage | ✅ | ⚠️ |

### Quality Analysis (10 tools)

| Tool | Description | ![C#][cs] | ![VB.NET][vb] |
|------|-------------|:---------:|:-------------:|
| `find_large_classes` | Types exceeding member-count or line-count thresholds | ✅ | ✅ |
| `get_public_api_surface` | Enumerate all public/protected types and members | ✅ | ✅ |
| `get_operators` | User-defined operators and conversions on a type | ✅ | ✅ |
| `find_obsolete_usage` | Every `[Obsolete]` call site grouped by severity | ✅ | ✅ |
| `get_call_graph` | BFS caller/callee graph, depth-bounded, cycle-safe | ✅ | ✅ |
| `find_async_violations` | `async void`, `.Result`/`.Wait()` blocking, fire-and-forget | ✅ | ✅ |
| `find_disposable_misuse` | `IDisposable` variables declared without `using` | ✅ | ✅ |
| `find_event_subscribers` | All `+=`/`-=` handlers for an event | ✅ | ✅ |
| `find_naming_violations` | PascalCase, `_field`, `IInterface` convention checks | ✅ | ⚠️ |
| `find_god_objects` | Large types with high outgoing namespace coupling | ✅ | ⚠️ |

### Decompilation (2 tools)

| Tool | Description | ![C#][cs] | ![VB.NET][vb] |
|------|-------------|:---------:|:-------------:|
| `peek_il` | Disassemble a method from a referenced assembly to MSIL bytecode | ✅ | ✅ |
| `inspect_external_assembly` | Browse the public API surface of any referenced assembly | ✅ | ✅ |

### Infrastructure (8 tools)

| Tool | Description | ![C#][cs] | ![VB.NET][vb] |
|------|-------------|:---------:|:-------------:|
| `health_check` | Server status | ✅ | ✅ |
| `load_solution` | Load `.sln`/`.slnx` (mixed-language solutions supported) | ✅ | ✅ |
| `sync_documents` | Sync file changes into loaded solution | ✅ | ✅ |
| `get_project_structure` | Solution structure (shows language per project) | ✅ | ✅ |
| `dependency_graph` | Project dependencies | ✅ | ✅ |
| `get_nuget_dependencies` | NuGet package listing per project | ✅ | ✅ |
| `get_source_generators` | List active source generators | ✅ | ✅ |
| `get_generated_code` | View generated source code | ✅ | ✅ |

## Architecture

```
MCP Client (AI Agent)
        | stdin/stdout (JSON-RPC 2.0)
        v
   DotNetLensMcp
   - Protocol handling
   - 74 AI-optimized tools
   - VB.NET language guards
        |
        v
Microsoft.CodeAnalysis (Roslyn)
  - MSBuildWorkspace
  - Microsoft.CodeAnalysis.CSharp
  - Microsoft.CodeAnalysis.VisualBasic   ← VB.NET support
  - SemanticModel / SymbolFinder
```

## Development

### Project Structure

| File / Folder | Purpose |
|--------------|---------|
| `src/RoslynService.cs` | Core workspace service, language helpers, compilation cache, source-generator handling |
| `src/RoslynService/Language/` | C#/VB strategy-pattern implementations; language-specific helpers |
| `src/RoslynService/Navigation/` | Symbol navigation tools (C#+VB) |
| `src/RoslynService/Analysis/` | Diagnostics, project structure, organize/format tools |
| `src/RoslynService/Refactoring/` | Refactoring tools (C# only, guarded) |
| `src/RoslynService/CodeActions/` | Code actions (C# only, guarded) |
| `src/RoslynService/CodeGeneration/` | Code generation tools (C# only, guarded) |
| `src/RoslynService/Compound/` | Data/control-flow compound tools (C# only, guarded) |
| `src/RoslynService/TypeDiscovery/` | Type/member queries (C#+VB) |
| `src/RoslynService/Discovery/` | Discovery tools for attributes, DI, reflection, packages, and source generators |
| `src/RoslynService/QualityAnalysis/` | Quality analysis tools — large classes, god objects, naming, async/disposable violations, call graph, etc. |
| `src/RoslynService/Decompilation/` | ILSpy-backed tools — peek IL, inspect external assembly; `PEFileCache`, `IlDisassemblerAdapter` helpers |
| `src/McpServer.cs` | MCP protocol loop and JSON-RPC handling |
| `src/ToolCatalog.cs` | MCP tool definitions and input schemas |
| `src/McpToolCallHandler.cs` | Tool routing and argument mapping |
| `src/ErrorCodes.cs` | Structured error code constants (includes `VB_NOT_SUPPORTED`) |

### Adding New Tools

1. **Add method to the appropriate `src/RoslynService/<Category>/` file**:
```csharp
public async Task<object> YourToolAsync(string filePath, int? param = null)
{
    EnsureSolutionLoaded();

    // Guard VB.NET if the tool uses C#-specific syntax APIs
    if (await GetCSharpOnlyToolErrorAsync(filePath, "your_tool") is { } unsupportedLanguageError)
        return unsupportedLanguageError;

    // Your logic...
    return CreateSuccessResponse(
        data: new { /* results */ },
        suggestedNextTools: new[] { "next_tool_hint" }
    );
}
```

2. **Add tool definition** to `src/ToolCatalog.cs`

3. **Add routing** in `src/McpToolCallHandler.cs`

4. **Write tests** — add a VB.NET variant in `tests/DotNetLensMcp.Tests/VbNetTests.cs` if the tool should support VB.NET, or assert `VB_NOT_SUPPORTED` if it should not. `ToolCatalogTests` verifies catalog entries are routed.

### VB.NET Guard Pattern

All C# syntax-level tools use a document-language guard immediately after `EnsureSolutionLoaded()`:

```csharp
if (await GetCSharpOnlyToolErrorAsync(filePath, "tool_name") is { } unsupportedLanguageError)
    return unsupportedLanguageError;
```

The guard resolves the Roslyn `Document` and checks `document.Project.Language`, with a `.vb` path fallback only when the document cannot be resolved. `VbNotSupportedResponse` returns a structured error with code `VB_NOT_SUPPORTED` and a descriptive message explaining which tools do support VB.NET.

### Running Tests

```bash
# All tests
dotnet test

# VB.NET tests only
dotnet test --filter "FullyQualifiedName~VbNetTests"
```

The VB.NET integration tests load `tests/DotNetLensMcp.Tests/TestSolutions/VbNetSample/` (a standalone VB.NET library) and `TestSolutions/MixedSample/` (a mixed C#+VB.NET solution).

## Credits

- **[sharplens-mcp](https://github.com/pzalutski-pixel/sharplens-mcp)** by Peter Zalutski — core MCP server architecture, Roslyn workspace integration, and C# analysis tools that this project is forked from.
- **[roslyn-codelens-mcp](https://github.com/MarcelRoozekrans/roslyn-codelens-mcp)** by Marcel Roozekrans — ILSpy-backed decompilation tools (`roslyn_peek_il`, `roslyn_inspect_external_assembly`) ported into this fork.
- **[ILSpy / ICSharpCode.Decompiler](https://github.com/icsharpcode/ILSpy)** — MSIL disassembly engine (MIT).

## License

Apache 2.0 — See [LICENSE](LICENSE) and [NOTICE](NOTICE) for details.
