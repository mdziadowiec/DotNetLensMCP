using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Organize using directives across multiple C# files in a project or the entire solution.
    /// </summary>
    public async Task<object> OrganizeUsingsBatchAsync(string? projectName, string? filePattern, bool preview = true)
    {
        EnsureSolutionLoaded();

        var projectsToProcess = string.IsNullOrEmpty(projectName)
            ? _solution!.Projects
            : _solution!.Projects.Where(p => p.Name == projectName);

        var processedFiles = new List<object>();
        var totalFiles = 0;
        var filesWithChanges = 0;

        foreach (var project in projectsToProcess)
        {
            var documents = project.Documents;

            // Apply file pattern filter if specified
            if (!string.IsNullOrEmpty(filePattern))
            {
                documents = documents.Where(d =>
                    d.FilePath != null && MatchesGlobPattern(Path.GetFileName(d.FilePath), filePattern));
            }

            // Skip non-C# files — organize_usings is C# only
            documents = documents.Where(IsCSharpDocument);

            foreach (var document in documents)
            {
                if (document.FilePath == null) continue;
                totalFiles++;

                try
                {
                    var syntaxTree = await document.GetSyntaxTreeAsync();
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync();
                    if (root is not CompilationUnitSyntax compilationUnit) continue;

                    var usings = compilationUnit.Usings;
                    if (usings.Count == 0) continue;

                    // Sort usings
                    var sortedUsings = usings
                        .OrderBy(u => u.Name?.ToString().StartsWith("System") == true ? 0 : 1)
                        .ThenBy(u => u.Name?.ToString())
                        .ToList();

                    // Check if anything changed
                    var hasChanges = !usings.SequenceEqual(sortedUsings);
                    if (!hasChanges) continue;

                    filesWithChanges++;

                    // Create new compilation unit with sorted usings
                    var newRoot = compilationUnit.WithUsings(SyntaxFactory.List(sortedUsings));
                    var newText = newRoot.ToFullString();

                    processedFiles.Add(new
                    {
                        filePath = FormatPath(document.FilePath),
                        fileName = Path.GetFileName(document.FilePath),
                        projectName = project.Name,
                        usingCount = usings.Count,
                        preview
                    });

                    // Write to disk if not preview
                    if (!preview)
                    {
                        await File.WriteAllTextAsync(document.FilePath, newText);
                    }
                }
                catch (Exception ex)
                {
                    processedFiles.Add(new
                    {
                        filePath = FormatPath(document.FilePath),
                        fileName = Path.GetFileName(document.FilePath),
                        projectName = project.Name,
                        error = ex.Message
                    });
                }
            }
        }

        if (!preview && filesWithChanges > 0)
            ClearWorkspaceCaches();

        return CreateSuccessResponse(
            data: new
            {
                totalFilesScanned = totalFiles,
                filesWithChanges,
                preview,
                files = processedFiles
            },
            suggestedNextTools: preview
                ? new[] { "organize_usings_batch with preview=false to apply changes" }
                : new[] { "get_diagnostics to verify no issues introduced", "format_document_batch for consistent formatting" },
            totalCount: totalFiles,
            returnedCount: processedFiles.Count
        );
    }
}
