using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> RenameSymbolAsync(
        string filePath,
        int line,
        int column,
        string newName,
        bool preview,
        int? maxFiles = null,
        string? verbosity = null)
    {
        EnsureSolutionLoaded();

        var maxFilesToShow = maxFiles ?? 20; // Default to 20 files to prevent huge outputs
        var verbosityLevel = verbosity?.ToLower() ?? "summary"; // Default to summary to prevent token explosions

        var (context, contextError) = await GetDocumentAnalysisContextAsync(filePath, line, column);
        if (contextError != null)
            return contextError;

        var position = GetPosition(context!.SyntaxTree, line, column);

        // Try to find symbol with improved logic and tolerance
        var (symbol, debugInfo) = TryFindSymbolForRename(
            context.SyntaxTree,
            context.SemanticModel,
            position,
            line,
            column);

        if (symbol == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                "No symbol found at position",
                hint: "Ensure cursor is on a symbol name (class, method, variable, etc.). Try adjusting the column position by ±1.",
                context: new { line, column, debug = debugInfo }
            );
        }

        // Validate new name
        if (string.IsNullOrWhiteSpace(newName))
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "New name cannot be empty",
                context: new { newName }
            );
        }

        // Check if symbol can be renamed (not extern, not from metadata)
        if (symbol.Locations.All(loc => !loc.IsInSource))
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Cannot rename symbol",
                hint: "Symbol is defined in metadata (external library), not in source code",
                context: new { symbolName = symbol.Name }
            );
        }

        // Perform rename
        var renameOptions = new Microsoft.CodeAnalysis.Rename.SymbolRenameOptions();
        var newSolution = await Microsoft.CodeAnalysis.Rename.Renamer.RenameSymbolAsync(
            _solution!,
            symbol,
            renameOptions,
            newName);

        // Get all changes
        var changes = new List<object>();
        var solutionChanges = newSolution.GetChanges(_solution!);

        var totalFiles = 0;
        var totalChanges = 0;

        foreach (var projectChanges in solutionChanges.GetProjectChanges())
        {
            foreach (var changedDocumentId in projectChanges.GetChangedDocuments())
            {
                totalFiles++;

                var oldDocument = _solution!.GetDocument(changedDocumentId);
                var newDocument = newSolution.GetDocument(changedDocumentId);

                if (oldDocument == null || newDocument == null)
                    continue;

                var oldText = await oldDocument.GetTextAsync();
                var newText = await newDocument.GetTextAsync();

                var textChanges = newText.GetTextChanges(oldText);
                totalChanges += textChanges.Count();

                // Only include detailed changes for first N files
                if (changes.Count < maxFilesToShow)
                {
                    if (verbosityLevel == "summary")
                    {
                        // Summary: Just file path and count
                        changes.Add(new
                        {
                            filePath = FormatPath(oldDocument.FilePath),
                            changeCount = textChanges.Count()
                        });
                    }
                    else if (verbosityLevel == "compact")
                    {
                        // Compact: Include change locations but no text
                        var documentChanges = new List<object>();
                        foreach (var textChange in textChanges.Take(20))
                        {
                            var lineSpan = oldText.Lines.GetLinePositionSpan(textChange.Span);
                            documentChanges.Add(new
                            {
                                line = lineSpan.Start.Line,
                                column = lineSpan.Start.Character
                            });
                        }

                        changes.Add(new
                        {
                            filePath = FormatPath(oldDocument.FilePath),
                            changeCount = textChanges.Count(),
                            changes = documentChanges,
                            truncated = textChanges.Count() > 20
                        });
                    }
                    else // "full" or any other value
                    {
                        // Full: Include old/new text for each change
                        var documentChanges = new List<object>();
                        foreach (var textChange in textChanges.Take(20))
                        {
                            var lineSpan = oldText.Lines.GetLinePositionSpan(textChange.Span);
                            documentChanges.Add(new
                            {
                                startLine = lineSpan.Start.Line,
                                startColumn = lineSpan.Start.Character,
                                endLine = lineSpan.End.Line,
                                endColumn = lineSpan.End.Character,
                                oldText = textChange.Span.Length > 0 ? oldText.ToString(textChange.Span) : "",
                                newText = textChange.NewText
                            });
                        }

                        changes.Add(new
                        {
                            filePath = FormatPath(oldDocument.FilePath),
                            changeCount = textChanges.Count(),
                            changes = documentChanges,
                            truncated = textChanges.Count() > 20
                        });
                    }
                }
            }
        }

        var filesShown = changes.Count;
        var filesHidden = totalFiles - filesShown;

        // If preview mode, just return the changes
        if (preview)
        {
            var verbosityHint = verbosityLevel == "summary"
                ? "Using verbosity='summary' (file paths + counts only). Use verbosity='compact' for locations or verbosity='full' for detailed text changes."
                : verbosityLevel == "compact"
                    ? "Using verbosity='compact' (locations only). Use verbosity='full' to see old/new text for each change."
                    : null;

            var hints = new List<string>();
            if (filesHidden > 0)
                hints.Add($"Showing first {maxFilesToShow} files. {filesHidden} more files will be changed. Use maxFiles parameter to see more.");
            if (verbosityHint != null)
                hints.Add(verbosityHint);
            if (hints.Count == 0)
                hints.Add("Set preview=false to apply these changes.");

            return CreateSuccessResponse(
                data: new
                {
                    symbolName = symbol.Name,
                    symbolKind = symbol.Kind.ToString(),
                    newName,
                    verbosity = verbosityLevel,
                    changes,
                    preview = true,
                    applied = false
                },
                suggestedNextTools: new[] { "rename_symbol with preview=false to apply changes" },
                totalCount: totalFiles,
                returnedCount: filesShown
            );
        }

        // Write changes to disk
        var workspace = _workspace!;
        if (workspace.TryApplyChanges(newSolution))
        {
            _solution = newSolution;
            ClearWorkspaceCaches();

            return CreateSuccessResponse(
                data: new
                {
                    symbolName = symbol.Name,
                    symbolKind = symbol.Kind.ToString(),
                    newName,
                    changes,
                    preview = false,
                    applied = true
                },
                suggestedNextTools: new[] { "get_diagnostics to verify no issues after rename" },
                totalCount: totalFiles,
                returnedCount: filesShown
            );
        }
        else
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Failed to apply changes",
                hint: "Workspace.TryApplyChanges returned false. Changes may conflict with current workspace state.",
                context: new { symbolName = symbol.Name, newName, totalFiles, totalChanges }
            );
        }
    }
}
