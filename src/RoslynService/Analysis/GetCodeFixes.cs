using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Get code fix suggestions for a specific diagnostic at a given position.
    /// </summary>
    public async Task<object> GetCodeFixesAsync(string filePath, string diagnosticId, int line, int column)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "get_code_fixes") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        var (context, contextError) = await GetDocumentAnalysisContextAsync(filePath, line, column);
        if (contextError != null)
            return contextError;

        var (diagnostic, nearbyDiagnostics) = FindDiagnosticAtPosition(context!, diagnosticId, line, column);

        var diagnosticsAtPosition = nearbyDiagnostics
            .Select(d => new
            {
                id = d.Id,
                message = d.GetMessage(),
                severity = d.Severity.ToString(),
                span = new
                {
                    start = d.Location.SourceSpan.Start,
                    end = d.Location.SourceSpan.End,
                    length = d.Location.SourceSpan.Length
                }
            })
            .ToList();

        if (diagnostic == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"No diagnostic with ID '{diagnosticId}' found",
                hint: diagnosticsAtPosition.Count > 0
                    ? $"Found {diagnosticsAtPosition.Count} other diagnostic(s) near this position. Try using one of their IDs."
                    : "No diagnostics found at this position. Run get_diagnostics to see all available diagnostics.",
                context: new { line, column, diagnosticsNearby = diagnosticsAtPosition }
            );
        }

        var lineSpan = diagnostic.Location.GetLineSpan();

        // Note: Actual code fix provider infrastructure would require CodeFixProvider registration
        // For now, we return diagnostic info and common fix suggestions based on diagnostic ID
        var suggestedFixes = GetCommonFixSuggestions(diagnostic.Id, diagnostic.GetMessage());

        return CreateSuccessResponse(
            data: new
            {
                diagnosticId = diagnostic.Id,
                message = diagnostic.GetMessage(),
                severity = diagnostic.Severity.ToString(),
                location = new
                {
                    filePath = FormatPath(lineSpan.Path),
                    startLine = lineSpan.StartLinePosition.Line,
                    startColumn = lineSpan.StartLinePosition.Character,
                    endLine = lineSpan.EndLinePosition.Line,
                    endColumn = lineSpan.EndLinePosition.Character
                },
                suggestedFixes
            },
            suggestedNextTools: new[]
            {
                "apply_code_fix to apply a fix automatically",
                "get_diagnostics to see other issues"
            }
        );
    }
}
