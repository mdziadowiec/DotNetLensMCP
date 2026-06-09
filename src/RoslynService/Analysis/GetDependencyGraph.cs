namespace DotNetLensMcp;

public partial class RoslynService
{
    public Task<object> GetDependencyGraphAsync(string? format)
    {
        EnsureSolutionLoaded();

        var projectGraph = new Dictionary<string, List<string>>();
        var allProjects = _solution!.Projects.ToList();

        // Build dependency graph
        foreach (var project in allProjects)
        {
            var dependencies = project.ProjectReferences
                .Select(pr => _solution!.GetProject(pr.ProjectId)?.Name)
                .Where(name => name != null)
                .Cast<string>()
                .ToList();

            projectGraph[project.Name] = dependencies;
        }

        // Detect cycles
        var cycles = DetectCycles(projectGraph);

        // Generate output based on format
        if (format?.ToLower() == "mermaid")
        {
            var mermaid = GenerateMermaidGraph(projectGraph);
            return Task.FromResult(CreateSuccessResponse(
                data: new
                {
                    format = "mermaid",
                    graph = mermaid,
                    hasCycles = cycles.Count > 0,
                    cycles
                },
                suggestedNextTools: cycles.Count > 0
                    ? new[] { "Resolve circular dependencies before building" }
                    : new[] { "get_project_structure for detailed project info" },
                totalCount: allProjects.Count,
                returnedCount: allProjects.Count
            ));
        }

        // Default: return structured data
        return Task.FromResult(CreateSuccessResponse(
            data: new
            {
                dependencies = projectGraph,
                hasCycles = cycles.Count > 0,
                cycles
            },
            suggestedNextTools: cycles.Count > 0
                ? new[] { "Resolve circular dependencies before building" }
                : new[] { "get_project_structure for detailed project info" },
            totalCount: allProjects.Count,
            returnedCount: allProjects.Count
        ));
    }
}
