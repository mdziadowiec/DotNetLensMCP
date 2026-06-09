using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Normalize whitespace formatting across all documents in a project or the entire solution.
    /// </summary>
    public async Task<object> FormatDocumentBatchAsync(string? projectName, bool includeTests = true, bool preview = true)
    {
        EnsureSolutionLoaded();

        var projectsToProcess = string.IsNullOrEmpty(projectName)
            ? _solution!.Projects
            : _solution!.Projects.Where(p => p.Name == projectName);

        var processedFiles = new List<object>();
        var totalFiles = 0;
        var filesFormatted = 0;

        foreach (var project in projectsToProcess)
        {
            // Filter out test projects if includeTests is false
            if (!includeTests && project.Name.Contains("Test", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var document in project.Documents)
            {
                if (document.FilePath == null) continue;
                totalFiles++;

                try
                {
                    var syntaxTree = await document.GetSyntaxTreeAsync();
                    if (syntaxTree == null) continue;

                    var root = await syntaxTree.GetRootAsync();

                    // Format the document using Roslyn's formatter
                    var formattedRoot = root.NormalizeWhitespace();
                    var formattedText = formattedRoot.ToFullString();

                    // Check if anything changed
                    var originalText = root.ToFullString();
                    var hasChanges = originalText != formattedText;

                    if (!hasChanges) continue;

                    filesFormatted++;

                    processedFiles.Add(new
                    {
                        filePath = FormatPath(document.FilePath),
                        fileName = Path.GetFileName(document.FilePath),
                        projectName = project.Name,
                        preview
                    });

                    // Write to disk if not preview
                    if (!preview)
                    {
                        await File.WriteAllTextAsync(document.FilePath, formattedText);
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

        if (!preview && filesFormatted > 0)
            ClearWorkspaceCaches();

        return CreateSuccessResponse(
            data: new
            {
                totalFilesScanned = totalFiles,
                filesFormatted,
                preview,
                files = processedFiles
            },
            suggestedNextTools: preview
                ? new[] { "format_document_batch with preview=false to apply changes" }
                : new[] { "get_diagnostics to verify no issues introduced" },
            totalCount: totalFiles,
            returnedCount: processedFiles.Count
        );
    }
}
