using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Get diagnostics (errors, warnings, hints) for a file, project, or the entire solution.
    /// </summary>
    public async Task<object> GetDiagnosticsAsync(string? filePath, string? projectPath, string? severity, bool includeHidden)
    {
        EnsureSolutionLoaded();

        var allDiagnostics = new List<Diagnostic>();

        if (!string.IsNullOrEmpty(filePath))
        {
            // Get diagnostics for specific file
            Document document;
            try
            {
                document = await GetDocumentAsync(filePath);
            }
            catch (FileNotFoundException)
            {
                return CreateErrorResponse(
                    ErrorCodes.FileNotInSolution,
                    $"File not found in solution: {filePath}",
                    hint: "Check the file path or reload the solution",
                    context: new { filePath }
                );
            }
            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel != null)
            {
                allDiagnostics.AddRange(semanticModel.GetDiagnostics());
            }
        }
        else if (!string.IsNullOrEmpty(projectPath))
        {
            // Get diagnostics for specific project
            var project = _solution!.Projects.FirstOrDefault(p => p.FilePath == projectPath);
            if (project != null)
            {
                var compilation = await GetProjectCompilationAsync(project);
                if (compilation != null)
                {
                    allDiagnostics.AddRange(compilation.GetDiagnostics());
                }
            }
        }
        else
        {
            // Get diagnostics for entire solution
            foreach (var project in _solution!.Projects)
            {
                var compilation = await GetProjectCompilationAsync(project);
                if (compilation != null)
                {
                    allDiagnostics.AddRange(compilation.GetDiagnostics());
                }
            }
        }

        // Filter by severity
        if (!string.IsNullOrEmpty(severity))
        {
            var severityEnum = Enum.Parse<DiagnosticSeverity>(severity, ignoreCase: true);
            allDiagnostics = allDiagnostics.Where(d => d.Severity == severityEnum).ToList();
        }

        // Filter hidden
        if (!includeHidden)
        {
            allDiagnostics = allDiagnostics.Where(d => d.Severity != DiagnosticSeverity.Hidden).ToList();
        }

        // Limit results
        allDiagnostics = allDiagnostics.Take(_maxDiagnostics).ToList();

        var diagnosticList = allDiagnostics.Select(d =>
        {
            var lineSpan = d.Location.GetLineSpan();
            return new
            {
                id = d.Id,
                severity = d.Severity.ToString(),
                message = d.GetMessage(),
                filePath = FormatPath(lineSpan.Path),
                line = lineSpan.StartLinePosition.Line,
                column = lineSpan.StartLinePosition.Character,
                endLine = lineSpan.EndLinePosition.Line,
                endColumn = lineSpan.EndLinePosition.Character
            };
        }).ToList();

        var errorCount = diagnosticList.Count(d => d.severity == "Error");
        var warningCount = diagnosticList.Count(d => d.severity == "Warning");

        return CreateSuccessResponse(
            data: new
            {
                errorCount,
                warningCount,
                diagnostics = diagnosticList
            },
            suggestedNextTools: errorCount > 0 || warningCount > 0
                ? new[]
                {
                    "get_code_fixes for a diagnostic to see available fixes",
                    "apply_code_fix to apply a fix automatically"
                }
                : new[]
                {
                    "No diagnostics found - solution is healthy"
                },
            totalCount: diagnosticList.Count,
            returnedCount: diagnosticList.Count
        );
    }
}
