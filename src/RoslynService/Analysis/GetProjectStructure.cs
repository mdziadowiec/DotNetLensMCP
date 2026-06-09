using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    private const int MaxProjectStructureReferences = 100;
    private const int MaxProjectStructureDocuments = 500;

    public Task<object> GetProjectStructureAsync(
        bool includeReferences,
        bool includeDocuments,
        string? projectNamePattern = null,
        int? maxProjects = null,
        bool summaryOnly = false)
    {
        EnsureSolutionLoaded();

        var projectsList = GetFilteredProjects(projectNamePattern, maxProjects).ToList();

        // Summary mode - just names and counts
        if (summaryOnly)
        {
            var summary = projectsList.Select(p => new
            {
                name = p.Name,
                documentCount = p.DocumentIds.Count,
                projectReferenceCount = p.ProjectReferences.Count(),
                language = p.Language
            }).ToList();

            return Task.FromResult(CreateSuccessResponse(
                data: new
                {
                    solutionPath = _solution!.FilePath,
                    projects = summary
                },
                suggestedNextTools: new[]
                {
                    "get_project_structure with summaryOnly=false for full details",
                    "get_project_structure with projectNamePattern to filter projects"
                },
                totalCount: _solution!.Projects.Count(),
                returnedCount: summary.Count
            ));
        }

        // Full mode - detailed info
        var projects = new List<object>();

        foreach (var project in projectsList)
        {
            var references = includeReferences
                ? project.MetadataReferences
                    .Take(MaxProjectStructureReferences)
                    .Select(r => r.Display ?? "Unknown")
                    .ToList()
                : null;

            var projectReferences = project.ProjectReferences
                .Select(pr => _solution!.GetProject(pr.ProjectId)?.Name ?? "Unknown")
                .ToList();

            var documents = includeDocuments
                ? project.Documents
                    .Take(MaxProjectStructureDocuments)
                    .Select(d => new
                    {
                        name = d.Name,
                        filePath = FormatPath(d.FilePath),
                        folders = d.Folders.ToList()
                    })
                    .ToList()
                : null;

            var referenceCount = project.MetadataReferences.Count();
            var documentCount = project.DocumentIds.Count;

            projects.Add(new
            {
                name = project.Name,
                filePath = FormatPath(project.FilePath),
                language = project.Language,
                outputPath = project.OutputFilePath,
                targetFramework = project.CompilationOptions?.Platform.ToString(),
                documentCount,
                referenceCount,
                references = includeReferences ? (referenceCount > MaxProjectStructureReferences ? references!.Concat(new[] { $"... and {referenceCount - MaxProjectStructureReferences} more" }).ToList() : references) : null,
                projectReferences,
                documents = includeDocuments ? (documentCount > MaxProjectStructureDocuments ? documents!.Concat(new[] { new { name = $"... and {documentCount - MaxProjectStructureDocuments} more documents", filePath = string.Empty, folders = new List<string>() } }).ToList() : documents) : null
            });
        }

        return Task.FromResult(CreateSuccessResponse(
            data: new
            {
                solutionPath = _solution!.FilePath,
                projects
            },
            suggestedNextTools: new[]
            {
                "get_diagnostics for a project to check for issues",
                "dependency_graph to visualize project dependencies"
            },
            totalCount: _solution!.Projects.Count(),
            returnedCount: projects.Count
        ));
    }

    private IEnumerable<Project> GetFilteredProjects(string? projectNamePattern, int? maxProjects)
    {
        var filteredProjects = _solution!.Projects.AsEnumerable();

        if (!string.IsNullOrEmpty(projectNamePattern))
        {
            var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(projectNamePattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            filteredProjects = filteredProjects.Where(p => regex.IsMatch(p.Name));
        }

        if (maxProjects.HasValue && maxProjects.Value > 0)
        {
            filteredProjects = filteredProjects.Take(maxProjects.Value);
        }

        return filteredProjects;
    }
}
