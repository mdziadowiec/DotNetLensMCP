using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Apply a code action by its title.
    /// </summary>
    public async Task<object> ApplyCodeActionByTitleAsync(
        string filePath,
        int line,
        int column,
        string title,
        int? endLine = null,
        int? endColumn = null,
        bool preview = true)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "apply_code_action_by_title") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        var (context, contextError) = await GetDocumentSyntaxContextAsync(filePath, line, column);
        if (contextError != null)
            return contextError;

        var (startPosition, endPosition) = GetPositionRange(context!.SyntaxTree, line, column, endLine, endColumn);

        var allActions = await GetAllCodeActionsAtPositionAsync(
            context.Document,
            startPosition,
            endPosition,
            includeCodeFixes: true,
            includeRefactorings: true);

        // Find action by title (case-insensitive)
        var matchingAction = allActions.FirstOrDefault(a =>
            a.action.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

        if (matchingAction.action == null)
        {
            // Try partial match
            matchingAction = allActions.FirstOrDefault(a =>
                a.action.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
        }

        if (matchingAction.action == null)
        {
            var availableTitles = allActions
                .Select(a => a.action.Title)
                .Distinct()
                .Take(10)
                .ToList();

            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"No code action found with title matching '{title}'",
                hint: availableTitles.Count > 0
                    ? $"Available actions: {string.Join(", ", availableTitles)}"
                    : "No actions available at this position. Try get_code_actions_at_position first.",
                context: new { title, availableCount = allActions.Count }
            );
        }

        var selectedAction = matchingAction.action;

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
                "Code action did not produce any changes",
                hint: "The selected action may not be applicable in this context.",
                context: new { actionTitle = selectedAction.Title }
            );
        }

        // Collect all changed documents (reuse pattern from ApplyCodeFixAsync)
        var changedDocuments = new List<object>();
        var solutionChanges = changedSolution.GetChanges(_solution!);

        foreach (var projectChanges in solutionChanges.GetProjectChanges())
        {
            // Added documents
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

                if (!preview && addedDoc.FilePath != null)
                {
                    await File.WriteAllTextAsync(addedDoc.FilePath, text.ToString());
                }
            }

            // Changed documents
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

                if (!preview && newDoc.FilePath != null)
                {
                    await File.WriteAllTextAsync(newDoc.FilePath, newText.ToString());
                }
            }

            // Removed documents
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
                actionTitle = selectedAction.Title,
                actionKind = matchingAction.kind,
                changedFiles = changedDocuments,
                preview
            },
            suggestedNextTools: preview
                ? new[] { $"apply_code_action_by_title with preview=false to apply changes to disk" }
                : new[] { "get_diagnostics to verify the action resolved any issues" },
            totalCount: changedDocuments.Count,
            returnedCount: changedDocuments.Count
        );
    }
}
