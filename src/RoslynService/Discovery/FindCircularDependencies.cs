namespace DotNetLensMcp;

public partial class RoslynService
{
    public Task<object> FindCircularDependenciesAsync(string? level = null)
    {
        EnsureSolutionLoaded();

        var analysisLevel = level?.ToLower() ?? "project";

        if (analysisLevel == "project")
        {
            var projectGraph = new Dictionary<string, List<string>>();
            foreach (var project in _solution!.Projects)
            {
                var dependencies = project.ProjectReferences
                    .Select(pr => _solution!.GetProject(pr.ProjectId)?.Name)
                    .Where(name => name != null)
                    .Cast<string>()
                    .ToList();
                projectGraph[project.Name] = dependencies;
            }

            var cycles = DetectCycles(projectGraph);
            return Task.FromResult(CreateSuccessResponse(
                data: new { level = analysisLevel, hasCycles = cycles.Count > 0, cycles, graph = projectGraph },
                suggestedNextTools: new[] { "dependency_graph", "get_project_structure" }
            ));
        }

        // Namespace level
        return FindNamespaceCircularDependenciesAsync();
    }

    private async Task<object> FindNamespaceCircularDependenciesAsync()
    {
        var namespaceGraph = new Dictionary<string, HashSet<string>>();

        foreach (var project in _solution!.Projects)
        {
            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;
            var isVbProject = project.Language == Microsoft.CodeAnalysis.LanguageNames.VisualBasic;
            var strategy = SyntaxStrategy(isVbProject);

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var root = await syntaxTree.GetRootAsync();

                var currentNamespace = strategy.ExtractNamespace(root);
                if (currentNamespace == null) continue;

                if (!namespaceGraph.ContainsKey(currentNamespace))
                    namespaceGraph[currentNamespace] = new HashSet<string>();

                foreach (var referencedNamespace in strategy.ExtractImports(root))
                {
                    if (referencedNamespace != null && referencedNamespace != currentNamespace)
                        namespaceGraph[currentNamespace].Add(referencedNamespace);
                }
            }
        }

        var graphForCycleDetection = namespaceGraph.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToList()
        );
        var cycles = DetectCycles(graphForCycleDetection);

        return CreateSuccessResponse(
            data: new
            {
                level = "namespace",
                hasCycles = cycles.Count > 0,
                cycles,
                namespaceCount = namespaceGraph.Count
            },
            suggestedNextTools: new[] { "dependency_graph", "get_project_structure" }
        );
    }
}
