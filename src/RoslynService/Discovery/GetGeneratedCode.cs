using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetGeneratedCodeAsync(string projectName, string generatedFileName)
    {
        EnsureSolutionLoaded();

        var project = _solution!.Projects.FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
        if (project == null)
        {
            return CreateErrorResponse(ErrorCodes.FileNotFound, $"Project '{projectName}' not found");
        }

        var compilation = await GetProjectCompilationAsync(project);
        if (compilation == null)
        {
            return CreateErrorResponse(ErrorCodes.AnalysisFailed, "Could not get compilation");
        }

        // Only search generated trees (not the project's hand-written documents).
        var projectDocumentPaths = new HashSet<string>(
            project.Documents.Where(d => d.FilePath != null).Select(d => d.FilePath!),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (projectDocumentPaths.Contains(tree.FilePath)) continue;

            if (tree.FilePath.Contains(generatedFileName, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(tree.FilePath).Contains(generatedFileName, StringComparison.OrdinalIgnoreCase))
            {
                var text = await tree.GetTextAsync();
                return CreateSuccessResponse(
                    data: new
                    {
                        fileName = Path.GetFileName(tree.FilePath),
                        hintPath = tree.FilePath,
                        sourceCode = text.ToString(),
                        lineCount = text.Lines.Count
                    },
                    suggestedNextTools: new[] { "get_diagnostics", "get_file_overview" }
                );
            }
        }

        return CreateErrorResponse(ErrorCodes.FileNotFound, $"Generated file '{generatedFileName}' not found in project '{projectName}'");
    }
}
