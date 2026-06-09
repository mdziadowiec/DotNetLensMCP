using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetSourceGeneratorsAsync(string? projectName = null)
    {
        EnsureSolutionLoaded();

        var generatorResults = new List<object>();
        var projects = projectName != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        foreach (var project in projects)
        {
            var generators = project.AnalyzerReferences
                .SelectMany(ar => ar.GetGenerators(project.Language))
                .ToList();

            if (generators.Count == 0) continue;

            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            // Post-generator compilation has extra syntax trees for generated code.
            // Identify them as trees whose FilePath is not one of the project's own documents.
            var projectDocumentPaths = new HashSet<string>(
                project.Documents.Where(d => d.FilePath != null).Select(d => d.FilePath!),
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            var generatedFiles = new List<object>();
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (projectDocumentPaths.Contains(tree.FilePath)) continue;

                var text = await tree.GetTextAsync();
                generatedFiles.Add(new
                {
                    fileName = Path.GetFileName(tree.FilePath),
                    hintPath = tree.FilePath,
                    lineCount = text.Lines.Count
                });
            }

            generatorResults.Add(new
            {
                projectName = project.Name,
                generators = generators.Select(g => new
                {
                    typeName = g.GetType().FullName,
                    assemblyName = g.GetType().Assembly.GetName().Name
                }).ToList(),
                generatedFiles
            });
        }

        return CreateSuccessResponse(
            data: new { projects = generatorResults },
            suggestedNextTools: new[] { "get_generated_code", "get_diagnostics" },
            totalCount: generatorResults.Count,
            returnedCount: generatorResults.Count
        );
    }
}
