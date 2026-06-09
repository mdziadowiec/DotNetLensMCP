using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Apply a code fix for a specific diagnostic, optionally previewing the changes first.
    /// </summary>
    public async Task<object> ApplyCodeFixAsync(
        string filePath,
        string diagnosticId,
        int line,
        int column,
        int? fixIndex = null,
        bool preview = true)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "apply_code_fix") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        var (context, contextError) = await GetDocumentAnalysisContextAsync(filePath, line, column);
        if (contextError != null)
            return contextError;

        if (context == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get document analysis context",
                context: new { filePath, line, column }
            );
        }

        var (diagnostic, _) = FindDiagnosticAtPosition(context, diagnosticId, line, column);

        if (diagnostic == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"No diagnostic with ID '{diagnosticId}' found at line {line}, column {column}",
                hint: "Run get_code_fixes first to verify the diagnostic exists at this location.",
                context: new { filePath, diagnosticId, line, column }
            );
        }

        // Get code actions from built-in code fix providers
        var codeActions = await GetCodeActionsForDiagnosticAsync(context.Document, diagnostic);

        if (codeActions.Count == 0)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                $"No code fixes available for diagnostic '{diagnosticId}'",
                hint: "This diagnostic may not have automated code fixes available. Try the suggestions from get_code_fixes.",
                context: new { diagnosticMessage = diagnostic.GetMessage(), suggestedFixes = GetCommonFixSuggestions(diagnosticId, diagnostic.GetMessage()) }
            );
        }

        // If no fixIndex specified, return available fixes
        if (fixIndex == null)
        {
            var availableFixes = codeActions.Select((action, index) => new
            {
                index,
                title = action.Title,
                equivalenceKey = action.EquivalenceKey
            }).ToList();

            return CreateSuccessResponse(
                data: new
                {
                    diagnosticId = diagnostic.Id,
                    message = diagnostic.GetMessage(),
                    availableFixes
                },
                suggestedNextTools: new[]
                {
                    $"apply_code_fix with fixIndex=0 and preview=true to preview the first fix",
                    $"apply_code_fix with fixIndex=0 and preview=false to apply the first fix"
                },
                totalCount: availableFixes.Count,
                returnedCount: availableFixes.Count
            );
        }

        // Validate fixIndex
        if (fixIndex < 0 || fixIndex >= codeActions.Count)
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                $"Invalid fixIndex {fixIndex}. Available range: 0 to {codeActions.Count - 1}",
                hint: "Call without fixIndex to list available fixes",
                context: new { fixIndex, availableFixCount = codeActions.Count }
            );
        }

        var selectedAction = codeActions[fixIndex.Value];

        // Apply the code action
        var operations = await selectedAction.GetOperationsAsync(CancellationToken.None);
        var changedSolution = _solution;

        foreach (var operation in operations)
        {
            if (operation is ApplyChangesOperation applyChangesOp)
            {
                changedSolution = applyChangesOp.ChangedSolution;
                break;
            }
        }

        if (changedSolution == null || changedSolution == _solution)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Code fix did not produce any changes",
                hint: "The selected fix may not be applicable in this context.",
                context: new { fixTitle = selectedAction.Title, fixIndex }
            );
        }

        // Collect all changed documents
        var changedDocuments = new List<object>();
        var solutionChanges = changedSolution.GetChanges(_solution!);

        foreach (var projectChanges in solutionChanges.GetProjectChanges())
        {
            // Check for added documents
            foreach (var addedDocId in projectChanges.GetAddedDocuments())
            {
                var addedDoc = changedSolution.GetDocument(addedDocId);
                if (addedDoc == null) continue;

                var text = await addedDoc.GetTextAsync();
                changedDocuments.Add(new
                {
                    filePath = FormatPath(addedDoc.FilePath) ?? $"NewFile_{addedDoc.Name}",
                    fileName = addedDoc.Name,
                    isNewFile = true,
                    newText = preview ? text.ToString() : null,
                    changeType = "Added"
                });

                // Write to disk if not preview
                if (!preview && addedDoc.FilePath != null)
                {
                    await File.WriteAllTextAsync(addedDoc.FilePath, text.ToString());
                }
            }

            // Check for changed documents
            foreach (var changedDocId in projectChanges.GetChangedDocuments())
            {
                var oldDoc = _solution!.GetDocument(changedDocId);
                var newDoc = changedSolution.GetDocument(changedDocId);
                if (oldDoc == null || newDoc == null) continue;

                var oldText = await oldDoc.GetTextAsync();
                var newText = await newDoc.GetTextAsync();

                var changes = newText.GetTextChanges(oldText).ToList();

                changedDocuments.Add(new
                {
                    filePath = FormatPath(newDoc.FilePath),
                    fileName = newDoc.Name,
                    isNewFile = false,
                    changeCount = changes.Count,
                    newText = preview ? newText.ToString() : null,
                    changes = preview ? changes.Select(c => new
                    {
                        span = new { start = c.Span.Start, end = c.Span.End, length = c.Span.Length },
                        oldText = oldText.ToString(c.Span),
                        newText = c.NewText
                    }).ToList() : null,
                    changeType = "Modified"
                });

                // Write to disk if not preview
                if (!preview && newDoc.FilePath != null)
                {
                    await File.WriteAllTextAsync(newDoc.FilePath, newText.ToString());
                }
            }

            // Check for removed documents
            foreach (var removedDocId in projectChanges.GetRemovedDocuments())
            {
                var removedDoc = _solution!.GetDocument(removedDocId);
                if (removedDoc == null) continue;

                changedDocuments.Add(new
                {
                    filePath = FormatPath(removedDoc.FilePath),
                    fileName = removedDoc.Name,
                    isNewFile = false,
                    changeType = "Removed"
                });

                // Delete file if not preview
                if (!preview && removedDoc.FilePath != null && File.Exists(removedDoc.FilePath))
                {
                    File.Delete(removedDoc.FilePath);
                }
            }
        }

        if (!preview && changedDocuments.Count > 0)
        {
            _solution = changedSolution;
            ClearWorkspaceCaches();
        }

        return CreateSuccessResponse(
            data: new
            {
                applied = !preview,
                diagnosticId = diagnostic.Id,
                fixTitle = selectedAction.Title,
                fixIndex = fixIndex.Value,
                changedFiles = changedDocuments,
                preview
            },
            suggestedNextTools: preview
                ? new[] { $"apply_code_fix with preview=false to apply changes to disk" }
                : new[] { "get_diagnostics to verify fix resolved the issue" },
            totalCount: changedDocuments.Count,
            returnedCount: changedDocuments.Count
        );
    }

    private async Task<List<CodeAction>> GetCodeActionsForDiagnosticAsync(Document document, Diagnostic diagnostic)
    {
        var codeActions = new List<CodeAction>();
        var codeFixProviders = GetBuiltInCodeFixProviders();

        foreach (var provider in codeFixProviders)
        {
            if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
                continue;

            var context = new CodeFixContext(
                document,
                diagnostic,
                (action, _) => codeActions.Add(action),
                CancellationToken.None);

            try
            {
                await provider.RegisterCodeFixesAsync(context);
            }
            catch
            {
                // Some providers may throw if they can't handle the diagnostic
                // Silently continue to next provider
            }
        }

        return codeActions;
    }
}
