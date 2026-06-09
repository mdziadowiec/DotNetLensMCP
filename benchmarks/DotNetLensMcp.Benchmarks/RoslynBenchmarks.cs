using BenchmarkDotNet.Attributes;
using Microsoft.Build.Locator;

namespace DotNetLensMcp.Benchmarks;

[MemoryDiagnoser]
public class RoslynBenchmarks
{
    private static readonly string SolutionPath;

    private RoslynService _service = null!;
    private string _roslynServicePath = null!;
    private int _createSuccessLine;
    private int _createSuccessColumn;
    private int _interfaceLine;
    private int _interfaceColumn;

    static RoslynBenchmarks()
    {
        MSBuildLocator.RegisterDefaults();
        SolutionPath = FindSolutionPath();
    }

    private static string FindSolutionPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DotNetLensMcp.sln")))
            dir = dir.Parent;

        return dir == null
            ? throw new InvalidOperationException(
                "Could not find DotNetLensMcp.sln starting from " + AppContext.BaseDirectory)
            : Path.Combine(dir.FullName, "DotNetLensMcp.sln");
    }

    [GlobalSetup]
    public async Task Setup()
    {
        _service = new RoslynService();
        await _service.LoadSolutionAsync(SolutionPath);

        var solutionDir = Path.GetDirectoryName(SolutionPath)!;
        _roslynServicePath = Path.Combine(solutionDir, "src", "RoslynService.cs");

        // Locate CreateSuccessResponse for callers/references benchmarks
        var lines = File.ReadAllLines(_roslynServicePath);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("private object CreateSuccessResponse"))
            {
                _createSuccessLine = i;
                _createSuccessColumn = lines[i].IndexOf("CreateSuccessResponse");
                break;
            }
        }

        // Locate ILanguageSyntaxStrategy for type/interface benchmarks
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("ILanguageSyntaxStrategy SyntaxStrategy"))
            {
                _interfaceLine = i;
                _interfaceColumn = lines[i].IndexOf("ILanguageSyntaxStrategy");
                break;
            }
        }
    }

    [Benchmark(Description = "load_solution: DotNetLensMcp.sln")]
    public async Task<object> SolutionLoading()
    {
        var svc = new RoslynService();
        return await svc.LoadSolutionAsync(SolutionPath);
    }

    [Benchmark(Description = "go_to_definition: ILanguageSyntaxStrategy")]
    public async Task<object> GoToDefinition()
    {
        return await _service.GoToDefinitionAsync(_roslynServicePath, _interfaceLine, _interfaceColumn);
    }

    [Benchmark(Description = "find_references: CreateSuccessResponse")]
    public async Task<object> FindReferences()
    {
        return await _service.FindReferencesAsync(_roslynServicePath, _createSuccessLine, _createSuccessColumn, 50);
    }

    [Benchmark(Description = "find_implementations: ILanguageSyntaxStrategy")]
    public async Task<object> FindImplementations()
    {
        return await _service.FindImplementationsAsync(_roslynServicePath, _interfaceLine, _interfaceColumn, 20);
    }

    [Benchmark(Description = "find_callers: CreateSuccessResponse")]
    public async Task<object> FindCallers()
    {
        return await _service.FindCallersAsync(_roslynServicePath, _createSuccessLine, _createSuccessColumn, 50);
    }

    [Benchmark(Description = "search_symbols: Roslyn*")]
    public async Task<object> SearchSymbols()
    {
        return await _service.SearchSymbolsAsync("Roslyn*", null, 20, null, 0);
    }

    [Benchmark(Description = "get_type_members: RoslynService")]
    public async Task<object> GetTypeMembers()
    {
        return await _service.GetTypeMembersAsync("RoslynService", false, null, "compact", 50);
    }

    [Benchmark(Description = "get_diagnostics: error severity")]
    public async Task<object> GetDiagnostics()
    {
        return await _service.GetDiagnosticsAsync(null, null, "error", false);
    }

    [Benchmark(Description = "get_complexity_metrics: RoslynService.cs")]
    public async Task<object> GetComplexityMetrics()
    {
        return await _service.GetComplexityMetricsAsync(_roslynServicePath, null, null, null);
    }

    [Benchmark(Description = "get_di_registrations: whole solution")]
    public async Task<object> GetDiRegistrations()
    {
        return await _service.GetDiRegistrationsAsync(null);
    }

    [Benchmark(Description = "find_reflection_usage: whole solution")]
    public async Task<object> FindReflectionUsage()
    {
        return await _service.FindReflectionUsageAsync(null, 100);
    }

    [Benchmark(Description = "find_unused_code: whole solution")]
    public async Task<object> FindUnusedCode()
    {
        return await _service.FindUnusedCodeAsync(null, false, false, null, 50);
    }

    [Benchmark(Description = "get_nuget_dependencies: whole solution")]
    public Task<object> GetNugetDependencies()
    {
        return _service.GetNuGetDependenciesAsync(null);
    }

    [Benchmark(Description = "dependency_graph: whole solution")]
    public Task<object> DependencyGraph()
    {
        return _service.GetDependencyGraphAsync(null);
    }

    [Benchmark(Description = "analyze_method: McpToolCallHandler.HandleAsync")]
    public async Task<object> AnalyzeMethod()
    {
        return await _service.AnalyzeMethodAsync("McpToolCallHandler", "HandleAsync", true, false, 20, 50);
    }

    [Benchmark(Description = "analyze_change_impact: CreateSuccessResponse")]
    public async Task<object> AnalyzeChangeImpact()
    {
        return await _service.AnalyzeChangeImpactAsync(
            _roslynServicePath, _createSuccessLine, _createSuccessColumn, "rename");
    }

    [Benchmark(Description = "get_type_hierarchy: ILanguageSyntaxStrategy")]
    public async Task<object> GetTypeHierarchy()
    {
        return await _service.GetTypeHierarchyAsync(_roslynServicePath, _interfaceLine, _interfaceColumn, 20);
    }
}
